@echo off
REM Compiles GameData\KSPFixSuspensionBounce\KSPFixSuspensionBounce.dll. Installing is up to you: copy the
REM GameData\KSPFixSuspensionBounce folder into the GameData of KSP.
setlocal
cd /d "%~dp0"

if not defined KSPDIR (
    echo ERROR: KSPDIR is not set. Point it at your KSP install folder.
    exit /b 1
)

dotnet build KSPFixSuspensionBounce.csproj -c Release -p:KSPDIR="%KSPDIR%"
exit /b %errorlevel%
