@echo off
echo ========================================
echo FuXing 插件卸载工具
echo ========================================

REM 检查管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 错误：需要管理员权限！
    echo 请以管理员身份运行此脚本。
    pause
    exit /b 1
)

echo 正在卸载FuXing插件...

REM 检查是否为64位系统
if exist "%WINDIR%\SysWOW64" (
    set REGASM_PATH="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    echo 使用64位RegAsm
) else (
    set REGASM_PATH="%WINDIR%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
    echo 使用32位RegAsm
)

echo.
echo 1. 卸载COM组件...
if exist "bin\Debug\FuXing.dll" (
    %REGASM_PATH% "bin\Debug\FuXing.dll" /unregister
    echo   ? COM组件卸载完成
) else (
    echo   ? DLL文件不存在，跳过COM卸载
)

echo.
echo 2. 清理注册表...
reg delete "HKEY_CURRENT_USER\Software\Microsoft\Office\Word\Addins\FuXing.Connect" /f >nul 2>&1
if %errorLevel% equ 0 (
    echo   ? 注册表项已删除
) else (
    echo   ? 注册表项不存在或删除失败
)

echo.
echo ========================================
echo 卸载完成！
echo ========================================
echo.
echo 建议操作：
echo 1. 重启 Microsoft Word
echo 2. 验证 福星 选项卡已消失
echo.
pause