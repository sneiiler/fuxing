@echo off
echo ========================================
echo FuXing 插件注册工具（无需管理员）
echo ========================================
echo.

if not exist "%~dp0bin\Debug\FuXing.dll" (
    echo 错误：找不到 bin\Debug\FuXing.dll
    echo 请先编译项目
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Register-UserCOM.ps1" -DllPath "%~dp0bin\Debug\FuXing.dll"

echo.
echo ========================================
echo 使用说明：
echo 1. 启动 Microsoft Word
echo 2. 在 Word 的 Ribbon 中查找 FuXing 选项卡
echo ========================================
pause
