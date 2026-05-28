@echo off
:: --- CONFIGURATION ---
:: Trage hier deinen Pfad zum Nuclear Option Verzeichnis ein:
SET "GAME_PATH=C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option"

:: Pfade zu den benötigten DLLs
SET "LIB_PATH=%GAME_PATH%\NuclearOption_Data\Managed"
SET "BEPINEX_PATH=%GAME_PATH%\BepInEx\core"

:: Pfad zum Compiler (Standard .NET 4.0 Compiler in Windows)
SET "CSC_EXE=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

echo Kompiliere TargetCamControl...

"%CSC_EXE%" /target:library /out:TargetCamControl.dll ^
    /reference:"%LIB_PATH%\Assembly-CSharp.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.CoreModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.PhysicsModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.UI.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.TextRenderingModule.dll" ^
    /reference:"%LIB_PATH%\Rewired_Core.dll" ^
    /reference:"%BEPINEX_PATH%\BepInEx.dll" ^
    /reference:"%BEPINEX_PATH%\0Harmony.dll" ^
    Plugin.cs Runner.cs

if %errorlevel% neq 0 (
    echo.
    echo Fehler beim Kompilieren!
    pause
    exit /b %errorlevel%
)

echo.
echo Fertig! TargetCamControl.dll wurde erstellt.
pause
