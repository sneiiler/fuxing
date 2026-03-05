@echo off
echo ========================================
echo FuXing 快速重新注册（无需管理员）
echo ========================================

echo 关闭 Word / WPS 进程...
taskkill /f /im WINWORD.EXE >nul 2>&1
taskkill /f /im wps.exe >nul 2>&1

echo 等待 3 秒...
timeout /t 3 /nobreak >nul

echo 注销旧注册...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Unregister-UserCOM.ps1"

echo 重新注册...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Register-UserCOM.ps1" -DllPath "%~dp0bin\Debug\FuXing.dll"

echo.
echo 注册完成！
echo.

REM 检测已安装的应用并启动
set "WORD_PATH=C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE"
set "WPS_PATH="
for /f "tokens=*" %%i in ('where /r "C:\Program Files (x86)\Kingsoft\WPS Office" wps.exe 2^>nul') do set "WPS_PATH=%%i"

if exist "%WORD_PATH%" (
    echo 正在启动 Word...
    start "" "%WORD_PATH%"
) else if defined WPS_PATH (
    echo 正在启动 WPS 文字...
    start "" "%WPS_PATH%"
) else (
    echo 未检测到 Word 或 WPS，请手动启动
)

echo.
echo 请查看是否出现福星选项卡
pause
