@echo off
setlocal

:: Try to find MSBuild — first check if we're in a Developer Command Prompt
where msbuild >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    set MSBUILD=msbuild
    goto :build
)

:: Check .NET Framework default location
set MSBUILD_PATH=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe
if exist "%MSBUILD_PATH%" (
    set MSBUILD=%MSBUILD_PATH%
    goto :build
)

:: 32-bit fallback
set MSBUILD_PATH=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
if exist "%MSBUILD_PATH%" (
    set MSBUILD=%MSBUILD_PATH%
    goto :build
)

echo MSBuild not found. Install Visual Studio Build Tools 2022:
echo   https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022
echo Then run this script from "Developer Command Prompt for VS 2022"
pause
exit /b 1

:build
echo Building AutoSaver...
%MSBUILD% AutoSaver.csproj /p:Configuration=Release /v:minimal /nologo

if %ERRORLEVEL% NEQ 0 (
    echo Build FAILED.
    pause
    exit /b 1
)

echo.
echo Build SUCCESS. Output: bin\Release\autosaver.exe
echo Copy autosaver.exe + generated-image-2.png to distribute.
pause
