/**
 * Arsist Engine - Unity Builder
 * Unity CLI連携によるヘッドレスビルド実行
 */
import { EventEmitter } from 'events';
import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';
import * as fs from 'fs-extra';

export interface UnityBuildConfig {
  projectPath: string;
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
  private unityProjectPath: string;
  private buildInProgress = false;
  private lastLogFile: string | null = null;

  constructor(unityPath: string) {
    super();
    this.unityPath = unityPath;
    this.unityProjectPath = path.join(__dirname, '../../..', 'UnityBackend', 'ArsistBuilder');
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
      if (!await fs.pathExists(this.unityProjectPath)) {
        return { valid: false, error: 'Unity backend project not found. Please run setup first.' };
      }

      const projectAssets = path.join(this.unityProjectPath, 'Assets');
      const projectSettings = path.join(this.unityProjectPath, 'ProjectSettings');
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

      if (!await fs.pathExists(config.projectPath)) {
        return { success: false, error: `Project path not found: ${config.projectPath}` };
      }

      if (!config.targetDevice || !config.buildTarget) {
        return { success: false, error: 'Invalid build configuration: targetDevice/buildTarget is required' };
      }

      await fs.ensureDir(config.outputPath);
      if (config.cleanOutput) {
        await fs.emptyDir(config.outputPath);
      }

      // Phase 1: データ転送
      this.emitProgress('transfer', 10, 'プロジェクトデータを転送中...');
      await this.transferProjectData(config);

      // Phase 2: パッチ適用
      this.emitProgress('patch', 30, 'SDKパッチを適用中...');
      await this.applyDevicePatch(config.targetDevice);

      // Phase 3: Unityビルド実行
      this.emitProgress('build', 50, 'Unityビルドを実行中...');
      const buildResult = await this.executeUnityBuild(config);

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

  private async transferProjectData(config: UnityBuildConfig): Promise<void> {
    const dataDir = path.join(this.unityProjectPath, 'Assets', 'ArsistGenerated');
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

    this.emit('log', '[Arsist] Project data transferred to Unity');
  }

  private async applyDevicePatch(targetDevice: string): Promise<void> {
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
        const destManifest = path.join(this.unityProjectPath, 'Assets', 'Plugins', 'Android', 'AndroidManifest.xml');
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
        const destScripts = path.join(this.unityProjectPath, 'Assets', 'Arsist', 'Editor', 'Adapters', path.basename(adapterDir));
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
      const destPackages = path.join(this.unityProjectPath, 'Packages');
      await fs.copy(packagesPatch, destPackages, { overwrite: true });
      this.emit('log', '[Arsist] Applied packages patch');
    }

    this.emit('log', `[Arsist] Device patch applied for ${targetDevice}`);
  }

  private async executeUnityBuild(config: UnityBuildConfig): Promise<{ success: boolean; error?: string }> {
    return new Promise((resolve) => {
      const timeoutMinutes = config.buildTimeoutMinutes ?? 60;
      const logFile = config.logFilePath || path.join(config.outputPath, 'unity_build.log');
      this.lastLogFile = logFile;

      const args = [
        '-batchmode',
        '-nographics',
        '-quit',
        '-projectPath', this.unityProjectPath,
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
        if (logIssues.errors.length > 0) {
          resolve({ success: false, error: logIssues.errors[0] });
          return;
        }

        if (code === 0) {
          resolve({ success: true });
        } else {
          resolve({ success: false, error: `Unity build failed with exit code ${code}` });
        }
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
        if (line.includes('Error') || line.includes('Exception')) {
          errors.push(line.trim());
        } else if (line.includes('Warning')) {
          warnings.push(line.trim());
        }
      }
    } catch (error) {
      this.emit('log', `[Arsist] Failed to read Unity log: ${(error as Error).message}`);
    }

    return { errors, warnings };
  }

  private async resolveAdapterDir(targetDevice: string): Promise<string | null> {
    const adaptersRoot = path.join(__dirname, '../../..', 'Adapters');
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
