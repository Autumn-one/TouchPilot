@echo off
setlocal
set "PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not exist "%PWSH%" set "PWSH=pwsh.exe"
"%PWSH%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" (
    echo TouchPilot build failed. The previous latest build has been kept.
    pause
)
exit /b %BUILD_EXIT%
