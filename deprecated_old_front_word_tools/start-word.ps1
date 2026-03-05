$wordPath = "C:\Program Files\Microsoft Office\Root\Office16\WINWORD.EXE"

# WPS 路径：搜索 Kingsoft 安装目录下的 wps.exe
$wpsPath = $null
$wpsBase = "C:\Program Files (x86)\Kingsoft\WPS Office"
if (Test-Path $wpsBase) {
    $wpsExe = Get-ChildItem -Path $wpsBase -Filter "wps.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($wpsExe) { $wpsPath = $wpsExe.FullName }
}

if (Test-Path $wordPath) {
    Start-Process -FilePath $wordPath
    Write-Host "Word 已启动，等待插件加载..."
} elseif ($wpsPath) {
    Start-Process -FilePath $wpsPath
    Write-Host "WPS 文字已启动，等待插件加载..."
} else {
    Write-Error "找不到 Word 或 WPS 文字"
    exit 1
}

Start-Sleep -Seconds 3
Write-Host "完成"
