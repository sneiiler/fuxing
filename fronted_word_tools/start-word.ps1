$wordPath = "C:\Program Files\Microsoft Office\Root\Office16\WINWORD.EXE"

if (-not (Test-Path $wordPath)) {
    Write-Error "找不到 Word：$wordPath"
    exit 1
}

Start-Process -FilePath $wordPath
Write-Host "Word 已启动，等待插件加载..."
Start-Sleep -Seconds 3
Write-Host "完成"
