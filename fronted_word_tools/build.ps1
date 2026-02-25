param(
    [string]$Configuration = "Debug"
)

$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"

if (-not (Test-Path $vswhere)) {
    Write-Error "找不到 vswhere.exe，请确认已安装 Visual Studio Build Tools 2022"
    exit 1
}

$msbuild = & $vswhere -latest -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1

if (-not $msbuild) {
    Write-Error "找不到 MSBuild.exe，请在 VS Build Tools 中安装 '.NET 桌面生成工具'"
    exit 1
}

Write-Host "MSBuild: $msbuild"
Write-Host "Configuration: $Configuration"
Write-Host ""

& $msbuild "$PSScriptRoot\FuXing.csproj" /p:Configuration=$Configuration /p:Platform=AnyCPU /m /nologo /verbosity:minimal
exit $LASTEXITCODE
