@echo off
echo ========================================
echo FuXing 快速重新注册（无需管理员）
echo ========================================

echo 关闭 Word 进程...
taskkill /f /im WINWORD.EXE >nul 2>&1

echo 等待 3 秒...
timeout /t 3 /nobreak >nul

echo 注销旧注册...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Unregister-UserCOM.ps1"

echo 重新注册...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Register-UserCOM.ps1" -DllPath "%~dp0bin\Debug\FuXing.dll"

echo.
echo 注册完成！正在启动 Word...
start "" "C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE"

echo.
echo 请查看 Word 中是否出现福星选项卡
pause
