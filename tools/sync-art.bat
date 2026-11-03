@echo off
rem Copies the sprites exported by Prop Studio into the game (game\art\props): sprites.json, the atlas pages and HUD portraits.
rem Export them first in Prop Studio:  godot --headless --path . -s res://tools/bb_export.gd -- --out=exports/belt_barricade
setlocal
if "%PROP_STUDIO%"=="" set "PROP_STUDIO=%~dp0..\..\Prop Studio"
set "SRC=%PROP_STUDIO%\exports\belt_barricade"
set "DST=%~dp0..\game\art\props"
if not exist "%SRC%\sprites\sprites.json" (
    echo No sprites at "%SRC%". Export them in Prop Studio first, or set PROP_STUDIO to its folder.
    exit /b 1
)
if not exist "%SRC%\atlas" (
    echo No atlas pages at "%SRC%\atlas". Export again with a Prop Studio that packs atlases.
    exit /b 1
)
robocopy "%SRC%\atlas" "%DST%\atlas" *.png /MIR /NJH /NJS /NDL /NFL >nul
if errorlevel 8 (
    echo Copy failed.
    exit /b 1
)
rem HUD portraits (the engineer slot in the toolbar).
robocopy "%SRC%\sprites" "%DST%\portraits" portrait_*.png /MIR /NJH /NJS /NDL /NFL >nul
if errorlevel 8 (
    echo Copy failed.
    exit /b 1
)
copy /y "%SRC%\sprites\sprites.json" "%DST%\sprites.json" >nul || (
    echo Copy failed.
    exit /b 1
)
echo Synced sprites from "%SRC%".
