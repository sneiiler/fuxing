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

echo ========================================
echo.