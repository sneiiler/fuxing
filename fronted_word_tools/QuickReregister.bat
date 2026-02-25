@echo off
echo ========================================
echo 快速重新注册FuXing
echo ========================================

echo 关闭Word进程...
taskkill /f /im WINWORD.EXE >nul 2>&1

echo 等待3秒...
timeout /t 3 /nobreak >nul

echo 重新注册插件...
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" "bin\Debug\FuXing.dll" /unregister >nul 2>&1
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" "bin\Debug\FuXing.dll" /codebase

echo.
echo 注册完成！正在启动Word...
start "" "C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE"

echo.
echo 请查看Word中是否出现福星选项卡
pause