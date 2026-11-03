@echo off
rem Builds and launches Belt & Barricade.
rem Any arguments are passed to the game, e.g.  play.bat --autostart=funnel  or  play.bat --sandbox
setlocal EnableDelayedExpansion
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo The .NET SDK was not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

call :find_godot
if not defined GODOT_EXE (
    echo Godot 4.7 .NET edition was not found.
    echo The standard Godot build cannot run this game; it needs the .NET ^(Mono^) edition.
    choice /c YN /m "Install it now with winget (GodotEngine.GodotEngine.Mono)"
    if errorlevel 2 exit /b 1
    winget install --id GodotEngine.GodotEngine.Mono -e --accept-package-agreements
    call :find_godot
)
if not defined GODOT_EXE (
    echo Still could not find Godot .NET. Set the GODOT environment variable to its .exe path and try again.
    pause
    exit /b 1
)
echo Using Godot: %GODOT_EXE%

echo Building...
dotnet build game\BeltAndBarricade.sln -nologo -v q
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)

rem First run: import the project's assets so the game can load them.
if not exist "game\.godot\imported" (
    echo Importing assets ^(first run only^)...
    "%GODOT_EXE%" --headless --path game --import >nul 2>&1
)

start "" "%GODOT_EXE%" --path game -- %*
exit /b 0

:find_godot
set "GODOT_EXE="
if defined GODOT if exist "%GODOT%" (
    set "GODOT_EXE=%GODOT%"
    exit /b 0
)
for /d %%D in ("%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*") do (
    for /f "delims=" %%F in ('dir /s /b "%%D\Godot*mono_win64.exe" 2^>nul') do set "GODOT_EXE=%%F"
)
exit /b 0
