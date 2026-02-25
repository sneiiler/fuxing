@echo off
setlocal

REM 获取参数
set "TARGET_DIR=%~1"
set "TARGET_NAME=%~2"
set "CONFIG_NAME=%~3"

echo ========================================
echo WordTools Post-Build 自动注册
echo ========================================
echo 配置: "%CONFIG_NAME%"
echo 输出路径: "%TARGET_DIR%"
echo 目标文件: "%TARGET_NAME%"

REM 复制Resources文件夹到输出目录
echo.
echo 复制资源文件...
if exist "Resources\" (
    if not exist "%TARGET_DIR%Resources\" mkdir "%TARGET_DIR%Resources\"
    xcopy "Resources\*.png" "%TARGET_DIR%Resources\" /Y /Q >nul 2>&1
    echo   ? 已复制图标文件到输出目录
) else (
    echo   ? Resources文件夹不存在
)

REM 只在Debug配置下自动注册
if /i not "%CONFIG_NAME%"=="Debug" (
    echo 跳过注册 - 只在Debug配置下自动注册
    goto :end
)

REM 检查管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ? 警告：没有管理员权限，跳过自动注册
    echo   请手动以管理员身份运行 RegisterPlugin.bat
    goto :end
)

REM 检查是否为64位系统
if exist "%WINDIR%\SysWOW64" (
    set REGASM_PATH="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
) else (
    set REGASM_PATH="%WINDIR%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
)

echo.
echo 自动注册COM组件...

REM 先卸载旧版本
%REGASM_PATH% "%TARGET_DIR%%TARGET_NAME%.dll" /unregister >nul 2>&1

REM 注册新版本
%REGASM_PATH% "%TARGET_DIR%%TARGET_NAME%.dll" /codebase >nul 2>&1

if %errorLevel% equ 0 (
    echo   ? COM组件注册成功
) else (
    echo   ? COM组件注册失败
)

:end
echo ========================================
echo.