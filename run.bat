@echo off
setlocal
cd /d "%~dp0"

if not exist "bin\Poe2ItemCompanion.exe" (
    echo Binary not found. Running build.bat first...
    call build.bat
    if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
)

echo Starting Path of Exile 1 / 2 Companion in System Tray...
start "" "bin\Poe2ItemCompanion.exe" %*
