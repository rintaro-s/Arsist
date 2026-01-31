const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawn } = require('child_process');

// Use the same compiled entrypoint Electron uses (dist/main/main/*)
const { UnityBuilder } = require(path.join('..', 'dist', 'main', 'main', 'unity', 'UnityBuilder'));

function readJson(p) {
  return JSON.parse(fs.readFileSync(p, 'utf8'));
}

function pickFirstExisting(paths) {
  for (const p of paths) {
    if (!p) continue;
    try {
      if (fs.existsSync(p)) return p;
    } catch {
      // ignore
    }
  }
  return null;
}

function safeFilename(s) {
  return String(s)
    .replace(/[:]/g, '-')
    .replace(/[\\/]/g, '_')
    .replace(/\s+/g, '_')
    .replace(/[^a-zA-Z0-9._-]/g, '');
}

function tryCopyFile(src, destDir, destName) {
  try {
    if (!src || !fs.existsSync(src)) return null;
    fs.mkdirSync(destDir, { recursive: true });
    const name = destName || path.basename(src);
    const dest = path.join(destDir, name);
    fs.copyFileSync(src, dest);
    return dest;
  } catch {
    return null;
  }
}

function tryTailFile(filePath, lines = 160) {
  try {
    if (!filePath || !fs.existsSync(filePath)) return null;
    const content = fs.readFileSync(filePath, 'utf8');
    const parts = content.split(/\r?\n/);
    return parts.slice(Math.max(0, parts.length - lines)).join('\n');
  } catch {
    return null;
  }
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function runCmd(cmd, args, { stdio = 'pipe' } = {}) {
  return new Promise((resolve, reject) => {
    const p = spawn(cmd, args, { stdio });
    let out = '';
    let err = '';
    if (p.stdout) p.stdout.on('data', (d) => (out += d.toString('utf8')));
    if (p.stderr) p.stderr.on('data', (d) => (err += d.toString('utf8')));
    p.on('error', reject);
    p.on('close', (code) => {
      if (code === 0) return resolve({ out, err });
      const e = new Error(`${cmd} ${args.join(' ')} exited with code ${code}`);
      e.out = out;
      e.err = err;
      reject(e);
    });
  });
}

function parseArgs(argv) {
  const args = { seconds: 25 };
  for (let i = 2; i < argv.length; i++) {
    if (argv[i] === '--seconds') {
      args.seconds = Number(argv[++i]) || args.seconds;
    }
  }
  return args;
}

(async () => {
  const { seconds } = parseArgs(process.argv);

  const storePath = path.join(os.homedir(), '.config', 'arsist-engine', 'config.json');
  const store = readJson(storePath);

  const unityPath = process.env.ARSIST_UNITY_PATH || store.unityPath;
  const outputPath = process.env.ARSIST_OUTPUT_PATH || store.defaultOutputPath;
  const sourceProjectPath = process.env.ARSIST_PROJECT_PATH || (store.recentProjects && store.recentProjects[0]);

  if (!unityPath) throw new Error('Unity path not found (set ARSIST_UNITY_PATH or configure in app)');
  if (!outputPath) throw new Error('Output path not found (set ARSIST_OUTPUT_PATH or configure in app)');
  if (!sourceProjectPath) throw new Error('Project path not found (set ARSIST_PROJECT_PATH or open a project in app)');

  const projectJsonPath = path.join(sourceProjectPath, 'project.json');
  const project = readJson(projectJsonPath);

  const unityWorkDir = path.join(outputPath, 'TempUnityProject');

  const diagDir = path.join(process.cwd(), 'diag_artifacts');
  const diagStamp = safeFilename(new Date().toISOString());

  const { remoteInput, ...androidBuild } = project.buildSettings || {};
  const manifestData = {
    projectId: project.id,
    projectName: project.name,
    version: project.version,
    appType: project.appType,
    targetDevice: project.targetDevice,
    arSettings: project.arSettings,
    uiAuthoring: project.uiAuthoring,
    uiCode: project.uiCode,
    designSystem: project.designSystem,
    build: androidBuild,
    buildSettings: project.buildSettings,
    remoteInput,
    exportedAt: new Date().toISOString(),
  };

  // Always write Unity's -logFile into the workspace so we can inspect it here.
  // (outputPath is often outside the workspace; reading it via tools is painful.)
  const logFilePath =
    process.env.ARSIST_UNITY_LOG ||
    path.join(diagDir, `unity_build_${diagStamp}.log`);

  const builder = new UnityBuilder(unityPath);
  builder.on('progress', (p) => console.log(`[progress] ${p.phase} ${p.progress}% ${p.message}`));
  builder.on('log', (l) => console.log(l));

  console.log('[Arsist] Building (XREAL One diag)...');
  const result = await builder.build({
    projectPath: unityWorkDir,
    sourceProjectPath,
    outputPath,
    targetDevice: project.targetDevice || 'XREAL_One',
    buildTarget: 'Android',
    developmentBuild: true,
    manifestData,
    scenesData: project.scenes || [],
    uiData: project.uiLayouts || [],
    logicCode: '',
    buildTimeoutMinutes: 60,
    logFilePath,
  });

  console.log('[Arsist] Build result:', result);
  if (!result.success) {
    // Best-effort: gather extra artifacts for troubleshooting.
    try {
      fs.mkdirSync(diagDir, { recursive: true });
    } catch {
      // ignore
    }

    const unityLogTail = tryTailFile(logFilePath, 200);
    if (unityLogTail) {
      const tailPath = path.join(diagDir, `unity_build_${diagStamp}.tail.txt`);
      try {
        fs.writeFileSync(tailPath, unityLogTail + '\n', 'utf8');
        console.log('[Arsist] Saved Unity log tail:', tailPath);
      } catch {
        // ignore
      }
      console.error('--- Unity log (tail) ---\n' + unityLogTail + '\n--- end ---');
    } else {
      console.warn('[Arsist] Unity log not found:', logFilePath);
    }

    const gradleRoot = path.join(unityWorkDir, 'Library', 'Bee', 'Android', 'Prj', 'IL2CPP', 'Gradle');
    const gradleProblems = path.join(gradleRoot, 'build', 'reports', 'problems', 'problems-report.html');
    const copiedProblems = tryCopyFile(gradleProblems, diagDir, `gradle_problems_${diagStamp}.html`);
    if (copiedProblems) console.log('[Arsist] Copied Gradle problems report:', copiedProblems);

    const gradleBuildLog = path.join(gradleRoot, 'build.log');
    const copiedBuildLog = tryCopyFile(gradleBuildLog, diagDir, `gradle_build_${diagStamp}.log`);
    if (copiedBuildLog) console.log('[Arsist] Copied Gradle build log:', copiedBuildLog);

    process.exit(1);
  }

  const apkPath = result.outputPath;
  if (!apkPath || !fs.existsSync(apkPath)) {
    console.error('[Arsist] Build succeeded but APK path is missing:', apkPath);
    process.exit(1);
  }

  const packageName =
    (project.buildSettings && project.buildSettings.packageName) ||
    (project.buildSettings && project.buildSettings.build && project.buildSettings.build.packageName) ||
    androidBuild.packageName ||
    'com.arsist.app';

  // ADB phase (best-effort)
  try {
    const { out } = await runCmd('adb', ['devices'], { stdio: 'pipe' });
    const hasDevice = out
      .split(/\r?\n/)
      .some((line) => /\tdevice\b/.test(line) && !/^\* daemon/.test(line));
    if (!hasDevice) {
      console.warn('[Arsist] adb is available but no devices are connected; skipping install/logcat.');
      process.exit(0);
    }
  } catch {
    console.warn('[Arsist] adb not found or not working; skipping install/logcat.');
    process.exit(0);
  }

  console.log('[Arsist] Installing APK via adb...');
  await runCmd('adb', ['install', '-r', apkPath], { stdio: 'inherit' });

  console.log('[Arsist] Clearing logcat...');
  try {
    await runCmd('adb', ['logcat', '-c'], { stdio: 'inherit' });
  } catch {
    // ignore
  }

  console.log('[Arsist] Launching app...');
  await runCmd('adb', ['shell', 'monkey', '-p', packageName, '-c', 'android.intent.category.LAUNCHER', '1'], { stdio: 'inherit' });

  console.log(`[Arsist] Capturing logcat for ${seconds}s...`);
  await sleep(seconds * 1000);

  const { out } = await runCmd('adb', ['logcat', '-d'], { stdio: 'pipe' });
  const interesting = out
    .split(/\r?\n/)
    .filter((line) =>
      /XREALXRLoader|XREALCallbackHandler|XREALError|ArsistModelLoader|Arsist\]|Unity\s|AndroidRuntime|E\/Unity|XR Plugin/i.test(line)
    )
    .join('\n');

  const diagPath = path.join(outputPath, `xreal_diag_${Date.now()}.log`);
  fs.writeFileSync(diagPath, interesting + '\n', 'utf8');
  console.log('[Arsist] Saved filtered logcat:', diagPath);

  process.exit(0);
})().catch((e) => {
  console.error('[Arsist] Fatal:', e && e.stack ? e.stack : e);
  process.exit(1);
});
