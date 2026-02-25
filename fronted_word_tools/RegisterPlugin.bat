@echo off
echo ========================================
echo FuXing 插件注册工具
echo ========================================

REM 检查管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 错误：需要管理员权限！
    echo 请以管理员身份运行此脚本。
    pause
    exit /b 1
)

echo 正在注册FuXing插件...

REM 检查是否为64位系统
if exist "%WINDIR%\SysWOW64" (
    set REGASM_PATH="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    echo 使用64位RegAsm
) else (
    set REGASM_PATH="%WINDIR%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
    echo 使用32位RegAsm
)

REM 检查DLL文件是否存在
if not exist "bin\Debug\FuXing.dll" (
    echo 错误：找不到 bin\Debug\FuXing.dll
    echo 请先编译项目
    pause
    exit /b 1
)

echo.
echo 1. 首先清理可能的旧注册...
%REGASM_PATH% "bin\Debug\FuXing.dll" /unregister >nul 2>&1
reg delete "HKEY_CURRENT_USER\Software\Microsoft\Office\Word\Addins\FuXing.Connect" /f >nul 2>&1

echo.
echo 2. 注册新的COM组件...
%REGASM_PATH% "bin\Debug\FuXing.dll" /codebase

if %errorLevel% equ 0 (
    echo.
    echo 3. 验证注册状态...
    reg query "HKEY_CURRENT_USER\Software\Microsoft\Office\Word\Addins\FuXing.Connect" >nul 2>&1
    if %errorLevel% equ 0 (
        echo   ? 注册表项已创建
        echo   ? FuXing.Connect 已注册
    ) else (
        echo   ? 注册表项未找到，可能注册失败
    )
    
    echo.
    echo ========================================
    echo 注册成功！
    echo ========================================
    echo.
    echo 使用说明：
    echo 1. 重启 Microsoft Word
    echo 2. 在 Word 的 Ribbon 中查找 "FuXing" 选项卡
    echo 3. 如果没有显示，请检查：
    echo    - Word 的信任设置
    echo    - 是否启用了COM加载项
    echo.
) else (
    echo.
    echo ========================================
    echo 注册失败！
    echo ========================================
    echo 错误代码: %errorLevel%
    echo 请检查：
    echo 1. 是否以管理员身份运行
    echo 2. DLL文件是否存在且未被占用
    echo 3. .NET Framework 4.7 是否已安装
    echo.
)

pause