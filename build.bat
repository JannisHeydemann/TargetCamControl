@echo off
:: --- CONFIGURATION ---
:: Trage hier deinen Pfad zum Nuclear Option Verzeichnis ein:
SET "GAME_PATH=C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option"

:: Pfade zu den benötigten DLLs
SET "LIB_PATH=%GAME_PATH%\NuclearOption_Data\Managed"
SET "BEPINEX_PATH=%GAME_PATH%\BepInEx\core"

:: Pfad zum Compiler (Roslyn)
SET "CSC_EXE=C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"

echo Kompiliere TargetCamControl...

"%CSC_EXE%" /target:library /out:TargetCamControl.dll ^
    /reference:"%LIB_PATH%\netstandard.dll" ^
    /reference:"%LIB_PATH%\mscorlib.dll" ^
    /reference:"%LIB_PATH%\System.dll" ^
    /reference:"%LIB_PATH%\System.Core.dll" ^
    /reference:"%LIB_PATH%\Mirage.dll" ^
    /reference:"%LIB_PATH%\Mirage.Components.dll" ^
    /reference:"%LIB_PATH%\Assembly-CSharp.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.CoreModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.SharedInternalsModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.AnimationModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.PhysicsModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.UI.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.UIModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.IMGUIModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.InputLegacyModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.TextRenderingModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.UnityWebRequestModule.dll" ^
    /reference:"%LIB_PATH%\UnityEngine.AssetBundleModule.dll" ^
    /reference:"%LIB_PATH%\Rewired_Core.dll" ^
    /reference:"%LIB_PATH%\Rewired_Windows.dll" ^
    /reference:"%BEPINEX_PATH%\BepInEx.dll" ^
    /reference:"%BEPINEX_PATH%\0Harmony.dll" ^
    Plugin.cs Runner.cs

if %errorlevel% neq 0 (
    echo.
    echo Fehler beim Kompilieren!
    exit /b %errorlevel%
)

echo.
echo Fertig! TargetCamControl.dll wurde erstellt.
exit /b 0
