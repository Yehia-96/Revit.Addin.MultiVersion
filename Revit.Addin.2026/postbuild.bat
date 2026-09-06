@chcp 65001 >nul
@echo off
setlocal EnableDelayedExpansion
:: Input arguments
set TARGET_DIR=%~1
set PROJECT_DIR=%~2
set CONFIGURATION=%~3

echo [INFO] Running Post-Build Script for Configuration: %CONFIGURATION%
echo [INFO] TARGET_DIR: %TARGET_DIR%
echo [INFO] PROJECT_DIR: %PROJECT_DIR%

set REVIT_YEAR=2026
set ADDIN_FILE=Revit.Addin.2026.addin

echo [INFO] Revit Year: %REVIT_YEAR%
echo [INFO] Addin File: %ADDIN_FILE%

:: COMMON DESTINATIONS
set APPDATA_DIR=!APPDATA!\Autodesk\Revit\Addins\%REVIT_YEAR%\PluginTrail
set PROGRAMDATA_DIR=C:\ProgramData\Autodesk\Revit\Addins\%REVIT_YEAR%\PluginTrail
set APPDATA_DIR_ADDIN=!APPDATA!\Autodesk\Revit\Addins\%REVIT_YEAR%\
set PROGRAMDATA_DIR_ADDIN=C:\ProgramData\Autodesk\Revit\Addins\%REVIT_YEAR%\

:: Ensure destination folders exist
if not exist "%APPDATA_DIR%" mkdir "%APPDATA_DIR%"
if not exist "%PROGRAMDATA_DIR%" mkdir "%PROGRAMDATA_DIR%"

::  DEBUG BUILD: Local copy only
if /I "%CONFIGURATION%"=="Debug" goto :DebugCopy
goto :CheckRelease

:DebugCopy
    echo [DEBUG] Detected Debug build for Revit %REVIT_YEAR% — running local copy logic...

    echo Copying DLLs...
    xcopy /Y /Q /I "!TARGET_DIR!\*.dll" "!APPDATA_DIR!\"
    xcopy /Y /Q /I "!TARGET_DIR!\*.dll" "!PROGRAMDATA_DIR!\"

    echo Copying .addin file...
    xcopy /Y /Q "!PROJECT_DIR!\%ADDIN_FILE%" "!APPDATA_DIR_ADDIN!\"
    xcopy /Y /Q "!PROJECT_DIR!\%ADDIN_FILE%" "!PROGRAMDATA_DIR_ADDIN!\"

    echo  Debug post-build copy completed for Revit %REVIT_YEAR%.
    goto :End

:CheckRelease
::  RELEASE BUILD: Publish to shared server
if /I "%CONFIGURATION%"=="Release" goto :ReleaseCopy
goto :End

:ReleaseCopy
    echo [RELEASE] Detected Release build for Revit %REVIT_YEAR% — publishing update...

    set "SERVER_DIR=%USERPROFILE%\Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\%REVIT_YEAR%\dependencies\"
    set "SERVER_DLL_DIR=!SERVER_DIR!\PluginTrail"
    set "SERVER_VERSION_DIR=%USERPROFILE%\Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\%REVIT_YEAR%"

    if not exist "%SERVER_DLL_DIR%" mkdir "%SERVER_DLL_DIR%"

    echo Copying DLLs to server...
    xcopy /Y /Q /I "!TARGET_DIR!\*.dll" "!SERVER_DLL_DIR!\" >nul

    echo Copying .addin to server...
    xcopy /Y /Q "!PROJECT_DIR!\%ADDIN_FILE%" "!SERVER_DIR!\" >nul

    echo Copying version.txt to server...
    xcopy /Y /Q "!PROJECT_DIR!\version.txt" "!SERVER_DIR!\" >nul

    echo Copying Version-docs.xlsx to server...
    xcopy /Y /Q "!PROJECT_DIR!\version-docs.xlsx" "!SERVER_VERSION_DIR!\" >nul
    echo [RELEASE] Publishing complete for Revit %REVIT_YEAR%.
    goto :End

:End
endlocal
