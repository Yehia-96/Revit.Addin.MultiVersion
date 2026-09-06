@echo off
setlocal EnableDelayedExpansion

set "REVIT_VERSION=2027"

:: ================================
::  Plugin Installer Config
:: ================================
set "PLUGIN_NAME=PluginTrail"
set "PLUGIN_VERSION=5.0.0.0"
set "TARGET_ADDIN=C:\ProgramData\Autodesk\Revit\Addins\%REVIT_VERSION%"
set "SOURCE_DIR=%~dp0Revit.Addin.%REVIT_VERSION%"
set "TARGET_DLL=%TARGET_ADDIN%\%PLUGIN_NAME%"

:: ================================
::  Title & Header
:: ================================
title Installing %PLUGIN_NAME% v%PLUGIN_VERSION% for Revit %REVIT_VERSION%

call :print "=============================================" "Green"
call :print "   %PLUGIN_NAME% v%PLUGIN_VERSION%" "Green"
call :print "   Revit %REVIT_VERSION% Plugin Installer" "Green"
call :print "=============================================" "Green"
echo.
timeout /t 1 >nul

:: ================================
::  Unblock DLLs Prompt
:: ================================
echo Do you want to unblock the plugin DLLs to avoid security prompts?
choice /M "Unblock plugin DLLs now?"

if errorlevel 2 (
    call :print "[INFO] Skipping DLL unblock." "Yellow"
) else (
    call :print "[INFO] Unblocking DLLs..." "Cyan"
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "Get-ChildItem '%SOURCE_DIR%\%PLUGIN_NAME%\*.dll' | Unblock-File"
)

timeout /t 1 >nul

:: ================================
::  Create Target Directory
:: ================================
if not exist "%TARGET_ADDIN%" (
    call :printwBg "[WARNING] Make sure that you have Revit %REVIT_VERSION% installed." "Black" "Yellow"
    echo.
    call :print "=============================================" "Yellow"
    call :print "   Exiting...." "Yellow"
    call :print "=============================================" "Yellow"
    pause
    exit /b
)
if not exist "%TARGET_DLL%" (
    call :print "[INFO] Creating directory: %TARGET_DLL%" "Cyan"
    mkdir "%TARGET_DLL%"
)
:: ================================
::  Copy DLLs
:: ================================
call :print "[INFO] Copying plugin DLLs..." "Cyan"
xcopy /Y /Q /I "%SOURCE_DIR%\%PLUGIN_NAME%\*.dll" "%TARGET_DLL%\" >nul

:: Count number of DLLs copied
for /f %%I in ('dir /b /a-d "%TARGET_DLL%\*.dll" ^| find /c /v ""') do set DLL_COUNT=%%I

if "%DLL_COUNT%"=="0" (
    call :printwBg "[ERROR] Failed to copy DLLs." "Black" "Red"
) else (
    call :printwBg "[OK] Copied %DLL_COUNT% DLL file(s)." "Black" "Green"
)

:: ================================
::  Copy .addin File
:: ================================
call :print "[INFO] Copying .addin file..." "Cyan"
xcopy /Y /Q "%SOURCE_DIR%\Revit.Addin.%REVIT_VERSION%.addin" "%TARGET_ADDIN%\" >nul

:: Count .addin file presence
if exist "%TARGET_ADDIN%\Revit.Addin.%REVIT_VERSION%.addin" (
    call :printwBg "[OK] .addin file copied." "Black" "Green"
) else (
    call :print "[ERROR] .addin file was not copied." "Red"
)

:: ================================
::  Finish
:: ================================
echo.
call :print "=============================================" "Green"
call :print " Plugin Installed Successfully!" "Green"
call :print "=============================================" "Green"
pause
exit /b

:: ================================
::  Function: print [text] [color]
:: ================================
:print
:: Usage: call :print "Text to print" "ColorName"
powershell -NoProfile -Command "Write-Host '%~1' -ForegroundColor %~2"
exit /b
:printwBg
powershell -NoProfile -Command "Write-Host '%~1' -ForegroundColor %~2 -BackgroundColor %~3"
exit /b
