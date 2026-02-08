# Unity コンパイルテストスクリプト
# Unityプロジェクトのコンパイルエラーをチェック

$ErrorActionPreference = "Continue"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Unity Compile Test" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

# パス設定
$projectRoot = "E:\github\Arsist"
$unityBackend = "$projectRoot\UnityBackend\ArsistBuilder"
$logFile = "$projectRoot\unity_compile_test.log"

# Unity.exeのパスを探す
$possibleUnityPaths = @(
    "C:\Program Files\Unity\Hub\Editor\2022.3.*\Editor\Unity.exe",
    "C:\Program Files\Unity\Hub\Editor\2023.1.*\Editor\Unity.exe",
    "C:\Program Files\Unity\Hub\Editor\2023.2.*\Editor\Unity.exe",
    "C:\Program Files\Unity\Editor\Unity.exe"
)

$unityExe = $null
foreach ($pattern in $possibleUnityPaths) {
    $found = Get-ChildItem -Path (Split-Path $pattern -Parent) -Filter "Unity.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        $unityExe = $found.FullName
        break
    }
}

# 環境変数からも探す
if (-not $unityExe -and $env:ARSIST_UNITY_PATH) {
    $unityExe = $env:ARSIST_UNITY_PATH
}

if (-not $unityExe) {
    Write-Host "❌ Unity.exe not found!" -ForegroundColor Red
    Write-Host "Please install Unity or set ARSIST_UNITY_PATH environment variable" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ Found Unity: $unityExe" -ForegroundColor Green

# 既存のログファイルを削除
if (Test-Path $logFile) {
    Remove-Item $logFile -Force
}

Write-Host "`nStarting Unity compile test..." -ForegroundColor Yellow
Write-Host "Project: $unityBackend" -ForegroundColor Gray
Write-Host "Log: $logFile" -ForegroundColor Gray

# Unityをbatchmodeで起動（コンパイルのみ）
$args = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $unityBackend,
    "-quit",
    "-logFile", $logFile
)

Write-Host "`nExecuting: $unityExe $args" -ForegroundColor Gray

$process = Start-Process -FilePath $unityExe -ArgumentList $args -NoNewWindow -Wait -PassThru

Write-Host "`n==================================" -ForegroundColor Cyan
Write-Host "Compile Test Result" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

# ログファイルの存在確認
if (-not (Test-Path $logFile)) {
    Write-Host "❌ Log file not created. Unity may have failed to start." -ForegroundColor Red
    exit 1
}

# エラーを検索
$errors = Get-Content $logFile | Select-String -Pattern "error CS|Error:|ERROR:|CompilerOutput:" -Context 0, 2

if ($errors.Count -eq 0) {
    Write-Host "✅ No compile errors found!" -ForegroundColor Green
    Write-Host "`nBuild should succeed. You can now run:" -ForegroundColor Cyan
    Write-Host "  npm run dev" -ForegroundColor White
    Write-Host "  or" -ForegroundColor White
    Write-Host "  node scripts/run-unity-build.js" -ForegroundColor White
    exit 0
} else {
    Write-Host "❌ Found $($errors.Count) compile errors:" -ForegroundColor Red
    Write-Host ""
    
    foreach ($error in $errors) {
        Write-Host $error.Line -ForegroundColor Red
    }
    
    Write-Host "`nFull log saved to: $logFile" -ForegroundColor Yellow
    Write-Host "Please check the errors above and fix them." -ForegroundColor Yellow
    exit 1
}
