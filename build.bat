@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo Building Path of Exile 1 / 2 Companion
echo ========================================================

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo Error: .NET Framework 4.8 csc.exe compiler not found at:
    echo %CSC%
    exit /b 1
)

if not exist "bin" mkdir "bin"
if not exist "bin\scripts" mkdir "bin\scripts"

echo Copying Lua scripts to bin\scripts...
copy /Y "scripts\pob_wrapper.lua" "bin\scripts\pob_wrapper.lua" >nul || exit /b 1
if not exist "bin\data" mkdir "bin\data"
copy /Y "data\ggg_passive_ids.tsv" "bin\data\ggg_passive_ids.tsv" >nul || exit /b 1
copy /Y "data\poe1_gem_acquisition.json" "bin\data\poe1_gem_acquisition.json" >nul || exit /b 1
copy /Y "data\poenavi-LICENSE.txt" "bin\data\poenavi-LICENSE.txt" >nul || exit /b 1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\compile-version.ps1" || exit /b 1

echo Compiling C# source files...
"%CSC%" /nologo /target:winexe /platform:x64 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll /out:"bin\Poe2ItemCompanion.exe" src\*.cs "bin\BuildVersion.cs"

if %ERRORLEVEL% equ 0 (
    echo.
    echo ========================================================
    echo BUILD SUCCESSFUL: bin\Poe2ItemCompanion.exe
    echo ========================================================
) else (
    echo.
    echo ========================================================
    echo BUILD FAILED!
    echo ========================================================
    exit /b 1
)
