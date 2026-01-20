/**
 * Arsist Engine - Unity Builder
 * Unity CLI連携によるヘッドレスビルド実行
 */
import { EventEmitter } from 'events';
import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';
import * as fs from 'fs-extra';
import { app } from 'electron';

export interface UnityBuildConfig {
  projectPath: string;
  /** Arsistプロジェクトのルート（project.jsonがあるフォルダ）。Assetsコピーに使用 */
  sourceProjectPath?: string;
  outputPath: string;
  targetDevice: string;
  buildTarget: 'Android' | 'iOS' | 'Windows' | 'MacOS';
  developmentBuild: boolean;
  manifestData: object;
  scenesData: object[];
  uiData: object[];
  logicCode: string;
  unityVersion?: string;
  unityPathOverride?: string;
  buildTimeoutMinutes?: number;
  logFilePath?: string;
  cleanOutput?: boolean;
}

export interface BuildProgress {
  phase: string;
  progress: number;
  message: string;
}

export class UnityBuilder extends EventEmitter {
  private unityPath: string;
  private currentProcess: ChildProcess | null = null;
  private unityTemplatePath: string;
  private buildInProgress = false;
  private lastLogFile: string | null = null;

  constructor(unityPath: string) {
    super();
    this.unityPath = unityPath;
    this.unityTemplatePath = path.join(__dirname, '../../..', 'UnityBackend', 'ArsistBuilder');
  }

  private resolveUnityTemplatePath(): { path: string | null; searched: string[] } {
    const searched: string[] = [];

    const cwd = process.cwd();
    searched.push(path.join(cwd, 'UnityBackend', 'ArsistBuilder'));

    try {
      const appPath = app.getAppPath();
      searched.push(path.join(appPath, 'UnityBackend', 'ArsistBuilder'));
    } catch {
      // ignore
    }

    searched.push(path.join(__dirname, '../../../..', 'UnityBackend', 'ArsistBuilder'));

    for (const p of searched) {
      if (fs.pathExistsSync(p)) {
        return { path: p, searched };
      }
    }

    return { path: null, searched };
  }

  private resolveRepoRoot(): { path: string | null; searched: string[] } {
    const searched: string[] = [];

    const candidates: string[] = [];
    candidates.push(process.cwd());

    try {
      const appPath = app.getAppPath();
      candidates.push(appPath);
      candidates.push(path.dirname(appPath));
    } catch {
      // ignore
    }

    // dist/main/main/unity -> repoRoot は ../../../..
    candidates.push(path.join(__dirname, '../../../..'));
    candidates.push(path.join(__dirname, '../../..'));

    for (const c of candidates) {
      const root = path.resolve(c);
      if (searched.includes(root)) continue;
      searched.push(root);
      if (fs.pathExistsSync(path.join(root, 'sdk')) && fs.pathExistsSync(path.join(root, 'Adapters'))) {
        return { path: root, searched };
      }
      if (fs.pathExistsSync(path.join(root, 'package.json')) && fs.pathExistsSync(path.join(root, 'UnityBackend'))) {
        return { path: root, searched };
      }
    }

    return { path: null, searched };
  }

  getUnityPath(): string {
    return this.unityPath;
  }

  setUnityPath(unityPath: string): void {
    this.unityPath = unityPath;
  }

  /**
   * Unity実行環境の検証
   */
  async validate(requiredVersion?: string): Promise<{ valid: boolean; version?: string; error?: string }> {
    try {
      // Unityパスの存在確認
      if (!await fs.pathExists(this.unityPath)) {
        return { valid: false, error: 'Unity executable not found' };
      }

      // バージョン取得
      const version = await this.getUnityVersion();
      
      // UnityBackendプロジェクトの存在確認
      const resolved = this.resolveUnityTemplatePath();
      if (!resolved.path) {
        return {
          valid: false,
          error: `Unity backend project not found. Please run setup first.\nSearched:\n- ${resolved.searched.join('\n- ')}`,
        };
      }
      this.unityTemplatePath = resolved.path;

      const projectAssets = path.join(this.unityTemplatePath, 'Assets');
      const projectSettings = path.join(this.unityTemplatePath, 'ProjectSettings');
      if (!await fs.pathExists(projectAssets) || !await fs.pathExists(projectSettings)) {
        return { valid: false, error: 'Unity backend project is incomplete (Assets/ProjectSettings missing)' };
      }

      if (requiredVersion) {
        const isCompatible = this.isUnityVersionCompatible(version, requiredVersion);
        if (!isCompatible) {
          return { valid: false, version, error: `Unity version mismatch. Required: ${requiredVersion}, Actual: ${version}` };
        }
      }

      return { valid: true, version };
    } catch (error) {
      return { valid: false, error: (error as Error).message };
    }
  }

  /**
   * ビルド実行
   */
  async build(config: UnityBuildConfig): Promise<{ success: boolean; outputPath?: string; error?: string }> {
    try {
      if (this.buildInProgress || this.currentProcess) {
        return { success: false, error: 'Build already in progress' };
      }

      this.buildInProgress = true;
      this.lastLogFile = null;
      this.emitProgress('prepare', 0, 'ビルド準備中...');

      const unityPathToUse = config.unityPathOverride || this.unityPath;
      if (unityPathToUse !== this.unityPath) {
        this.unityPath = unityPathToUse;
      }

      const validation = await this.validate(config.unityVersion);
      if (!validation.valid) {
        return { success: false, error: validation.error || 'Unity validation failed' };
      }

      if (!config.projectPath || !config.outputPath) {
        return { success: false, error: 'Invalid build configuration: projectPath/outputPath is required' };
      }

      // projectPath は「作業用Unityプロジェクト」を展開するディレクトリ。
      // まだ存在しないのが正常なので、ここで作成する。
      try {
        if (await fs.pathExists(config.projectPath)) {
          const stat = await fs.stat(config.projectPath);
          if (!stat.isDirectory()) {
            return { success: false, error: `Project path is not a directory: ${config.projectPath}` };
          }
        } else {
          await fs.ensureDir(config.projectPath);
        }
      } catch (error) {
        return { success: false, error: `Failed to prepare project path: ${(error as Error).message}` };
      }

      if (!config.targetDevice || !config.buildTarget) {
        return { success: false, error: 'Invalid build configuration: targetDevice/buildTarget is required' };
      }

      await fs.ensureDir(config.outputPath);
      if (config.cleanOutput) {
        await fs.emptyDir(config.outputPath);
      }

      // Phase 1: Unityワークディレクトリ準備
      this.emitProgress('prepare-unity', 5, 'Unityプロジェクトを準備中...');
      const unityProjectPath = await this.prepareUnityProject(config.projectPath);

      // Phase 2: データ転送
      this.emitProgress('transfer', 10, 'プロジェクトデータを転送中...');
      await this.transferProjectData(unityProjectPath, config);

      // Phase 3: パッチ適用
      this.emitProgress('patch', 30, 'SDKパッチを適用中...');
      await this.applyDevicePatch(unityProjectPath, config.targetDevice);

      // Phase 3.5: 必須SDKをUnityプロジェクトへ組み込み
      this.emitProgress('sdk', 40, '必須SDKを確認/組み込み中...');
      await this.integrateRequiredSdks(unityProjectPath, config.targetDevice);

      // Phase 4: Unityビルド実行
      this.emitProgress('build', 50, 'Unityビルドを実行中...');
      let buildResult = await this.executeUnityBuild(unityProjectPath, config);

      // OpenXR は初回インポート直後のバッチビルドで
      // "OpenXR Settings found in project but not yet loaded. Please build again." が出ることがある。
      // その場合は同一プロジェクトで 1 回だけリトライして前に進める。
      if (!buildResult.success && /OpenXR Settings found in project but not yet loaded/i.test(buildResult.error || '')) {
        this.emit('log', '[Arsist] OpenXR settings not loaded yet. Retrying Unity build once...');
        buildResult = await this.executeUnityBuild(unityProjectPath, config);
      }

      if (!buildResult.success) {
        return { success: false, error: buildResult.error };
      }

      // Phase 4: 出力ファイル確認
      this.emitProgress('verify', 90, 'ビルド結果を確認中...');
      const outputFile = await this.verifyBuildOutput(config);

      if (!outputFile) {
        return { success: false, error: 'Build output not found' };
      }

      this.emitProgress('complete', 100, 'ビルド完了！');
      return { success: true, outputPath: outputFile };

    } catch (error) {
      return { success: false, error: (error as Error).message };
    } finally {
      this.buildInProgress = false;
    }
  }

  /**
   * ビルドキャンセル
   */
  cancel(): void {
    if (this.currentProcess) {
      this.currentProcess.kill('SIGTERM');
      this.currentProcess = null;
      this.emit('log', '[Arsist] Build cancelled by user');
    }
  }

  // ========================================
  // Private Methods
  // ========================================

  private async getUnityVersion(): Promise<string> {
    return new Promise((resolve, reject) => {
      const process = spawn(this.unityPath, ['-version'], { stdio: 'pipe' });
      let output = '';

      process.stdout?.on('data', (data) => {
        output += data.toString();
      });

      process.on('close', (code) => {
        if (code === 0) {
          resolve(output.trim());
        } else {
          reject(new Error('Failed to get Unity version'));
        }
      });

      process.on('error', reject);
    });
  }

  private isUnityVersionCompatible(actual: string, required: string): boolean {
    const actualVersion = this.normalizeUnityVersion(actual);
    const requiredVersion = this.normalizeUnityVersion(required);
    if (!actualVersion || !requiredVersion) return true;
    return this.compareVersions(actualVersion, requiredVersion) >= 0;
  }

  private normalizeUnityVersion(version: string): string | null {
    const match = version.match(/\d+\.\d+\.\d+(?:f\d+)?/);
    return match ? match[0] : null;
  }

  private compareVersions(a: string, b: string): number {
    const parse = (v: string) => v.replace('f', '.').split('.').map(n => parseInt(n, 10));
    const av = parse(a);
    const bv = parse(b);
    const len = Math.max(av.length, bv.length);
    for (let i = 0; i < len; i++) {
      const diff = (av[i] || 0) - (bv[i] || 0);
      if (diff !== 0) return diff;
    }
    return 0;
  }

  private async prepareUnityProject(workingDir: string): Promise<string> {
    await fs.ensureDir(workingDir);
    await fs.emptyDir(workingDir);
    await fs.copy(this.unityTemplatePath, workingDir);
    return workingDir;
  }

  private async transferProjectData(unityProjectPath: string, config: UnityBuildConfig): Promise<void> {
    const dataDir = path.join(unityProjectPath, 'Assets', 'ArsistGenerated');
    await fs.ensureDir(dataDir);

    if (!config.manifestData || !config.scenesData || !config.uiData) {
      throw new Error('Invalid project data: manifest/scenes/ui is required');
    }

    // マニフェスト
    await fs.writeJSON(
      path.join(dataDir, 'manifest.json'),
      config.manifestData,
      { spaces: 2 }
    );

    // シーンデータ
    await fs.writeJSON(
      path.join(dataDir, 'scenes.json'),
      config.scenesData,
      { spaces: 2 }
    );

    // UIデータ
    await fs.writeJSON(
      path.join(dataDir, 'ui_layouts.json'),
      config.uiData,
      { spaces: 2 }
    );

    // 生成されたロジックコード
    const scriptsDir = path.join(dataDir, 'Scripts');
    await fs.ensureDir(scriptsDir);
    if (config.logicCode) {
      await fs.writeFile(
        path.join(scriptsDir, 'GeneratedLogic.cs'),
        config.logicCode
      );
    }

    // Arsistプロジェクト内AssetsをUnityプロジェクトにコピー（実アセットとしてUnityに取り込ませる）
    if (config.sourceProjectPath) {
      const sourceAssets = path.join(config.sourceProjectPath, 'Assets');
      if (await fs.pathExists(sourceAssets)) {
        const destAssets = path.join(unityProjectPath, 'Assets', 'ArsistProjectAssets');
        await fs.ensureDir(destAssets);
        await fs.copy(sourceAssets, destAssets, { overwrite: true });
        this.emit('log', '[Arsist] Project Assets copied into Unity (Assets/ArsistProjectAssets)');
      } else {
        this.emit('log', `[Arsist] Project Assets folder not found: ${sourceAssets}`);
      }
    }

    this.emit('log', '[Arsist] Project data transferred to Unity');
  }

  private async applyDevicePatch(unityProjectPath: string, targetDevice: string): Promise<void> {
    const adapterDir = await this.resolveAdapterDir(targetDevice);
    
    if (!adapterDir || !await fs.pathExists(adapterDir)) {
      this.emit('log', `[Arsist] No specific patch for ${targetDevice}, using default settings`);
      return;
    }

    // AndroidManifest パッチ
    const manifestCandidates = [
      path.join(adapterDir, 'AndroidManifest.xml'),
      path.join(adapterDir, 'Manifest', 'AndroidManifest.xml'),
    ];
    for (const manifestPatch of manifestCandidates) {
      if (await fs.pathExists(manifestPatch)) {
        const destManifest = path.join(unityProjectPath, 'Assets', 'Plugins', 'Android', 'AndroidManifest.xml');
        await fs.ensureDir(path.dirname(destManifest));
        await fs.copy(manifestPatch, destManifest, { overwrite: true });
        this.emit('log', '[Arsist] Applied AndroidManifest patch');
        break;
      }
    }

    // Editor Scripts パッチ
    const scriptsCandidates = [
      path.join(adapterDir, 'Scripts'),
      path.join(adapterDir, 'Editor'),
      adapterDir,
    ];
    for (const scriptsPatch of scriptsCandidates) {
      if (await fs.pathExists(scriptsPatch)) {
        const destScripts = path.join(unityProjectPath, 'Assets', 'Arsist', 'Editor', 'Adapters', path.basename(adapterDir));
        await fs.ensureDir(destScripts);
        const entries = await fs.readdir(scriptsPatch);
        const csFiles = entries.filter((f) => f.endsWith('.cs'));
        for (const file of csFiles) {
          await fs.copy(path.join(scriptsPatch, file), path.join(destScripts, file), { overwrite: true });
        }
        if (csFiles.length > 0) {
          this.emit('log', '[Arsist] Applied editor scripts patch');
        }
        break;
      }
    }

    // Packages パッチ
    const packagesPatch = path.join(adapterDir, 'Packages');
    if (await fs.pathExists(packagesPatch)) {
      const destPackages = path.join(unityProjectPath, 'Packages');
      await fs.copy(packagesPatch, destPackages, { overwrite: true });
      this.emit('log', '[Arsist] Applied packages patch');
    }

    this.emit('log', `[Arsist] Device patch applied for ${targetDevice}`);
  }

  private isXrealTarget(targetDevice: string): boolean {
    const normalized = (targetDevice || '').toLowerCase();
    return normalized.includes('xreal');
  }

  private async integrateRequiredSdks(unityProjectPath: string, targetDevice: string): Promise<void> {
    if (this.isXrealTarget(targetDevice)) {
      await this.integrateXrealSdk(unityProjectPath);
    }
  }

  private async integrateXrealSdk(unityProjectPath: string): Promise<void> {
    const resolvedRepo = this.resolveRepoRoot();
    if (!resolvedRepo.path) {
      throw new Error(
        `XREAL SDK not found (repo root not detected).\nSearched:\n- ${resolvedRepo.searched.join('\n- ')}`
      );
    }

    const sdkSourceDir = path.join(resolvedRepo.path, 'sdk', 'com.xreal.xr', 'package');
    const sdkPackageJson = path.join(sdkSourceDir, 'package.json');

    if (!await fs.pathExists(sdkPackageJson)) {
      throw new Error(
        `XREAL SDK not found. Place the XREAL UPM package at sdk/com.xreal.xr/package (package.json missing).\nLooked for:\n- ${sdkPackageJson}`
      );
    }

    const destDir = path.join(unityProjectPath, 'Packages', 'com.xreal.xr');
    await fs.ensureDir(path.dirname(destDir));
    await fs.copy(sdkSourceDir, destDir, { overwrite: true });

    const manifestPath = path.join(unityProjectPath, 'Packages', 'manifest.json');
    if (!await fs.pathExists(manifestPath)) {
      throw new Error(`Unity manifest.json not found: ${manifestPath}`);
    }

    const manifest = await fs.readJSON(manifestPath);
    const dependencies = (manifest.dependencies ?? {}) as Record<string, string>;
    // Packages/manifest.json からの相対パス（同じフォルダ内のcom.xreal.xr）
    dependencies['com.xreal.xr'] = 'file:com.xreal.xr';
    manifest.dependencies = dependencies;
    await fs.writeJSON(manifestPath, manifest, { spaces: 2 });

    this.emit('log', '[Arsist] Embedded XREAL SDK: Packages/com.xreal.xr (manifest.json updated)');
  }

  private async executeUnityBuild(unityProjectPath: string, config: UnityBuildConfig): Promise<{ success: boolean; error?: string }> {
    return new Promise((resolve) => {
      const timeoutMinutes = config.buildTimeoutMinutes ?? 60;
      const logFile = config.logFilePath || path.join(config.outputPath, 'unity_build.log');
      this.lastLogFile = logFile;

      const args = [
        '-batchmode',
        '-nographics',
        '-quit',
        '-projectPath', unityProjectPath,
        '-executeMethod', 'Arsist.Builder.ArsistBuildPipeline.BuildFromCLI',
        `-buildTarget`, config.buildTarget,
        `-outputPath`, config.outputPath,
        `-targetDevice`, config.targetDevice,
        `-developmentBuild`, config.developmentBuild ? 'true' : 'false',
        '-logFile', logFile,
      ];

      this.emit('log', `[Unity] Starting build: ${this.unityPath} ${args.join(' ')}`);

      this.currentProcess = spawn(this.unityPath, args, {
        stdio: ['ignore', 'pipe', 'pipe'],
      });

      this.currentProcess.stdout?.on('data', (data) => {
        const lines = data.toString().split('\n');
        for (const line of lines) {
          if (line.trim()) {
            this.emit('log', `[Unity] ${line}`);
            this.parseUnityProgress(line);
          }
        }
      });

      this.currentProcess.stderr?.on('data', (data) => {
        const message = data.toString();
        this.emit('log', `[Unity Error] ${message}`);
      });

      const timeout = setTimeout(() => {
        if (this.currentProcess) {
          this.emit('log', `[Unity] Build timed out after ${timeoutMinutes} minutes`);
          this.currentProcess.kill('SIGKILL');
        }
      }, timeoutMinutes * 60 * 1000);

      this.currentProcess.on('close', async (code) => {
        clearTimeout(timeout);
        this.currentProcess = null;
        const logIssues = await this.readUnityLogIssues(logFile);
        if (logIssues.errors.length > 0) {
          logIssues.errors.forEach((line) => this.emit('log', `[Unity Error] ${line}`));
        }

        const pickBestError = (errors: string[]) => {
          const prefer = errors.find((e) => /error\s+CS\d+/i.test(e));
          if (prefer) return prefer;
          const ignoreLic = errors.find((e) => !/Access token is unavailable/i.test(e));
          if (ignoreLic) return ignoreLic;
          return errors[0];
        };

        // Unity はログに例外が出ても exit code 0 で成功することがあるため、
        // 成功コードの場合はビルド成功を優先する。
        if (code === 0) {
          resolve({ success: true });
          return;
        }

        if (logIssues.errors.length > 0) {
          resolve({ success: false, error: pickBestError(logIssues.errors) });
          return;
        }

        resolve({ success: false, error: `Unity build failed with exit code ${code}` });
      });

      this.currentProcess.on('error', (error) => {
        clearTimeout(timeout);
        this.currentProcess = null;
        resolve({ success: false, error: error.message });
      });
    });
  }

  private parseUnityProgress(line: string): void {
    // Unityのログから進捗を解析
    if (line.includes('Compiling shader')) {
      this.emitProgress('build', 55, 'シェーダーをコンパイル中...');
    } else if (line.includes('Building scene')) {
      this.emitProgress('build', 60, 'シーンをビルド中...');
    } else if (line.includes('Packaging assets')) {
      this.emitProgress('build', 70, 'アセットをパッケージ中...');
    } else if (line.includes('Creating APK')) {
      this.emitProgress('build', 80, 'APKを作成中...');
    } else if (line.includes('Build completed')) {
      this.emitProgress('build', 85, 'ビルド処理完了');
    }
  }

  private async verifyBuildOutput(config: UnityBuildConfig): Promise<string | null> {
    const possibleOutputs = [
      path.join(config.outputPath, `${path.basename(config.projectPath)}.apk`),
      path.join(config.outputPath, 'build.apk'),
      path.join(config.outputPath, 'ArsistApp.apk'),
    ];

    for (const output of possibleOutputs) {
      if (await fs.pathExists(output)) {
        return output;
      }
    }

    // ディレクトリ内の.apkファイルを探す
    try {
      const files = await fs.readdir(config.outputPath);
      const apk = files.find(f => f.endsWith('.apk'));
      if (apk) {
        return path.join(config.outputPath, apk);
      }
    } catch (e) {
      // ignore
    }

    return null;
  }

  private async readUnityLogIssues(logFile: string): Promise<{ errors: string[]; warnings: string[] }> {
    const errors: string[] = [];
    const warnings: string[] = [];

    if (!await fs.pathExists(logFile)) {
      return { errors, warnings };
    }

    try {
      const content = await fs.readFile(logFile, 'utf-8');
      const lines = content.split(/\r?\n/);
      for (const line of lines) {
        const t = line.trim();
        if (!t) continue;

        if (/Scripts have compiler errors\./i.test(t)) {
          errors.push(t);
          continue;
        }

        // Unity/CSC は "error CSxxxx" のように小文字になることがある
        // ただし "ValidationExceptions.json" のようなファイル名もあるため、Exception 判定はコロン付きに限定する
        if (/(^|\s)error(\s|:)/i.test(t) || /Exception\s*:/i.test(t) || /BuildFailedException\s*:/i.test(t)) {
          errors.push(t);
          continue;
        }

        if (/(^|\s)warning(\s|:)/i.test(t)) {
          warnings.push(t);
        }
      }
    } catch (error) {
      this.emit('log', `[Arsist] Failed to read Unity log: ${(error as Error).message}`);
    }

    return { errors, warnings };
  }

  private async resolveAdapterDir(targetDevice: string): Promise<string | null> {
    const resolvedRepo = this.resolveRepoRoot();
    const adaptersRoot = resolvedRepo.path ? path.join(resolvedRepo.path, 'Adapters') : path.join(__dirname, '../../..', 'Adapters');
    if (!await fs.pathExists(adaptersRoot)) return null;

    const direct = path.join(adaptersRoot, targetDevice);
    if (await fs.pathExists(direct)) return direct;

    const normalizedTarget = targetDevice.replace(/[-\s]/g, '_').toLowerCase();
    const entries = await fs.readdir(adaptersRoot);
    for (const entry of entries) {
      const normalizedEntry = entry.replace(/[-\s]/g, '_').toLowerCase();
      if (normalizedEntry === normalizedTarget) {
        return path.join(adaptersRoot, entry);
      }
    }

    return null;
  }

  private emitProgress(phase: string, progress: number, message: string): void {
    this.emit('progress', { phase, progress, message } as BuildProgress);
  }
}
