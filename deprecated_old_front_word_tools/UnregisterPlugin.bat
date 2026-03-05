@echo off
echo ========================================
echo FuXing 插件卸载工具（无需管理员）
echo ========================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Unregister-UserCOM.ps1"

echo.
echo ========================================
echo 卸载完成！请重启 Word 生效。
echo ========================================
pause
