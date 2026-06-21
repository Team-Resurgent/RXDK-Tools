@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%install-dotnet-runtime.ps1"

if not exist "%PS1%" (
  echo Could not find install-dotnet-runtime.ps1 next to this script.
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
exit /b %ERRORLEVEL%
