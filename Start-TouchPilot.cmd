@echo off
setlocal
set "APP_DIR=%~dp0artifacts\latest"
if not exist "%APP_DIR%\TouchPilot.exe" goto missing
if not exist "%APP_DIR%\TouchPilot.ControlPanel.exe" goto missing
start "" /d "%APP_DIR%" "%APP_DIR%\TouchPilot.ControlPanel.exe"
exit /b %ERRORLEVEL%

:missing
echo The latest TouchPilot build is missing.
echo Run "%~dp0Build-TouchPilot.cmd" first.
pause
exit /b 1
