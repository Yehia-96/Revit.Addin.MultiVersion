@chcp 65001 >nul
@echo off
setlocal EnableDelayedExpansion 

:: Input arguments
set "PROJECT_DIR=D:\Database_parameters\VisualStudio\Database_Creator\ProjectsApp\Revit.Addin.2024"
set "TARGET_DIR=!PROJECT_DIR!\bin\Debug"
set "VERSION_FILE_PATH=!PROJECT_DIR!\version.txt"

set "SERVER_DIR=%USERPROFILE%\Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\2024\dependencies"
set "SERVER_DLL_DIR=!SERVER_DIR!\PluginTrail"
set "SERVER_VERSION_FILE=!SERVER_DIR!"

echo [INFO] Running Local to Server Script
echo [INFO] TARGET_DIR: !TARGET_DIR!
echo [INFO] PROJECT_DIR: !PROJECT_DIR!
echo [INFO] VERSION_FILE_PATH: !VERSION_FILE_PATH!
echo [INFO] SERVER_DIR: !SERVER_DIR!
echo [INFO] SERVER_DLL_DIR: !SERVER_DLL_DIR!
echo [INFO] SERVER_VERSION_FILE: !SERVER_VERSION_FILE!

xcopy /Y /Q /I "!TARGET_DIR!\*.dll" "!SERVER_DLL_DIR!"
xcopy /Y /Q "!VERSION_FILE_PATH!" "!SERVER_VERSION_FILE!"
xcopy /Y /Q "!PROJECT_DIR!\Revit.Addin.2024.addin" "!SERVER_DIR!"
