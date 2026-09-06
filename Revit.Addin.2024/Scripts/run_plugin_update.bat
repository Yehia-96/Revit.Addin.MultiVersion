:: This script is not used but kept for reference.
@echo off
echo ================================
echo    Revit Plugin Update Start
echo ================================
timeout /t 3 >nul

echo Updating Revit Plugin...

:: Paths
set SERVER_DIR=X:\Abdelazziz\Plugin_Versions\dependencies
set ADDIN_FILE=X:\Abdelazziz\Plugin_Versions\dependencies\Revit.Addin.2024.addin
set LOCAL_ADDIN_DIR=C:\ProgramData\Autodesk\Revit\Addins\2024
set LOCAL_DLL_DIR=C:\ProgramData\Autodesk\Revit\Addins\2024\PluginTrail

:: Ensure target directory exists
if not exist "%LOCAL_DLL_DIR%" mkdir "%LOCAL_DLL_DIR%"

:: Copy DLLs
xcopy /Y /Q /I "%SERVER_DIR%\PluginTrail\*.dll" "%LOCAL_DLL_DIR%\"

:: Copy .addin file
xcopy /Y /Q "%ADDIN_FILE%" "%LOCAL_ADDIN_DIR%\"

echo  Plugin update complete.
pause
exit






