@echo off
rem Copies the building sprites exported by Prop Studio into the game (game\art\buildings).
rem Export them first in Prop Studio:  godot --headless --path . -s res://tools/bb_export.gd -- --out=exports/belt_barricade
setlocal
if "%PROP_STUDIO%"=="" set "PROP_STUDIO=%~dp0..\..\Prop Studio"
set "SRC=%PROP_STUDIO%\exports\belt_barricade\sprites"
if not exist "%SRC%\sprites.json" (
    echo No sprites at "%SRC%". Export them in Prop Studio first, or set PROP_STUDIO to its folder.
    exit /b 1
)
robocopy "%SRC%" "%~dp0..\game\art\buildings" /MIR /NJH /NJS /NDL /NFL >nul
if errorlevel 8 (
    echo Copy failed.
    exit /b 1
)
echo Synced sprites from "%SRC%".
