@echo off
setlocal

set "TARGET_DIR=%~1"
set "TARGET_NAME=%~2"
set "CONFIG_NAME=%~3"
set "PROJECT_DIR=%~dp0"

echo ========================================
echo FuXing Post-Build
echo ========================================
echo Config:  "%CONFIG_NAME%"
echo OutDir:  "%TARGET_DIR%"
echo Target:  "%TARGET_NAME%"

REM -- Copy Resources to output --
echo.
echo Copying resources...
if exist "%PROJECT_DIR%Resources\" (
    if not exist "%TARGET_DIR%Resources\" mkdir "%TARGET_DIR%Resources\"
    xcopy "%PROJECT_DIR%Resources\*.png" "%TARGET_DIR%Resources\" /Y /Q
    if %errorLevel% equ 0 (
        echo   [OK] Resources copied
    ) else (
        echo   [FAIL] Resource copy failed
    )
) else (
    echo   [FAIL] Resources folder not found: %PROJECT_DIR%Resources\
)

REM -- COM registration (Debug only) --
if /i not "%CONFIG_NAME%"=="Debug" (
    echo Skip COM register - Debug only
    goto :end
)

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [WARN] No admin rights, skip COM register
    echo   Run RegisterPlugin.bat manually as admin
    goto :end
)

if exist "%WINDIR%\SysWOW64" (
    set REGASM_PATH="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
) else (
    set REGASM_PATH="%WINDIR%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
)

echo.
echo Registering COM...

%REGASM_PATH% "%TARGET_DIR%%TARGET_NAME%.dll" /unregister >nul 2>&1
%REGASM_PATH% "%TARGET_DIR%%TARGET_NAME%.dll" /codebase >nul 2>&1

if %errorLevel% equ 0 (
    echo   [OK] COM registered
) else (
    echo   [FAIL] COM registration failed
)

:end
echo ========================================
echo.