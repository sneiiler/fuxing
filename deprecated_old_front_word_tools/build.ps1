param(
    [string]$Configuration = "Debug"
)

$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$InnoSetupPath = "C:\Program Files (x86)\Inno Setup 6\Compil32.exe"
$SetupFile = "$PSScriptRoot\setup.iss"

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
$buildExit = $LASTEXITCODE

if ($buildExit -ne 0) {
    exit $buildExit
}

# Debug 模式自动进行用户级 COM 注册（无需管理员权限）
if ($Configuration -eq "Debug") {
    $dllPath = "$PSScriptRoot\bin\Debug\FuXing.dll"
    if (Test-Path $dllPath) {
        Write-Host ""
        Write-Host "========================================"
        Write-Host "用户级 COM 注册（无需管理员）"
        Write-Host "========================================"
        & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\Register-UserCOM.ps1" -DllPath $dllPath
    }
}

# Release 模式自动调用 Inno Setup 编译安装包
if ($Configuration -eq "Release") {
    if (-not (Test-Path $InnoSetupPath)) {
        Write-Warning "找不到 Inno Setup 6，跳过安装包编译"
        Write-Warning "请安装 Inno Setup 6: https://jrsoftware.org/isdl.php"
    } elseif (-not (Test-Path $SetupFile)) {
        Write-Warning "找不到 setup.iss，跳过安装包编译"
    } else {
        Write-Host ""
        Write-Host "========================================"
        Write-Host "调用 Inno Setup 编译安装包"
        Write-Host "========================================"

        & $InnoSetupPath /cc $SetupFile

        if ($LASTEXITCODE -eq 0) {
            $outputDir = "$PSScriptRoot\Output"
            $setupExe = Get-ChildItem -Path $outputDir -Filter "FuXing_Setup*.exe" | Select-Object -First 1
            if ($setupExe) {
                Write-Host ""
                Write-Host "安装包已生成: $($setupExe.FullName)" -ForegroundColor Green
                Write-Host "大小: $([math]::Round($setupExe.Length / 1MB, 2)) MB" -ForegroundColor Gray
            }
        } else {
            Write-Error "Inno Setup 编译失败"
            exit 1
        }
    }
}

exit 0
