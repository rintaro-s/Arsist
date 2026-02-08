# Unity Build Monitor
# ビルドログをリアルタイムで監視

param(
    [string]$LogPath = "E:\files\ARs\0208\2\unity_build.log",
    [int]$TimeoutMinutes = 10
)

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Unity Build Monitor" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Log: $LogPath" -ForegroundColor Gray
Write-Host "Timeout: $TimeoutMinutes minutes" -ForegroundColor Gray
Write-Host ""

$startTime = Get-Date
$lastSize = 0

while ($true) {
    # タイムアウトチェック
    $elapsed = (Get-Date) - $startTime
    if ($elapsed.TotalMinutes -gt $TimeoutMinutes) {
        Write-Host "`n❌ Timeout ($TimeoutMinutes minutes)" -ForegroundColor Red
        exit 1
    }
    
    # ログファイルの存在確認
    if (Test-Path $LogPath) {
        $currentSize = (Get-Item $LogPath).Length
        
        if ($currentSize -gt $lastSize) {
            # 新しい内容を表示
            $content = Get-Content $LogPath -Raw
            $newContent = $content.Substring($lastSize)
            Write-Host $newContent -NoNewline
            
            $lastSize = $currentSize
            
            # 成功/失敗の判定
            if ($content -match "Build completed successfully|DisplayProgressbar: Build Successful") {
                Write-Host "`n==================================" -ForegroundColor Green
                Write-Host "✅ Build Success!" -ForegroundColor Green
                Write-Host "==================================" -ForegroundColor Green
                exit 0
            }
            elseif ($content -match "Build failed|Error:|CompilerOutput:|-\s+error\s+CS") {
                Write-Host "`n==================================" -ForegroundColor Red
                Write-Host "❌ Build Failed - Check errors above" -ForegroundColor Red
                Write-Host "==================================" -ForegroundColor Red
                
                # エラー行を抽出
                $errors = $content -split "`n" | Where-Object { $_ -match "error|Error|ERROR|CompilerOutput:" }
                if ($errors) {
                    Write-Host "`nErrors found:" -ForegroundColor Yellow
                    $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
                }
                
                exit 1
            }
        }
    }
    else {
        Write-Host "." -NoNewline -ForegroundColor Gray
    }
    
    Start-Sleep -Seconds 2
}
