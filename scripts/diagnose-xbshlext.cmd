@echo off
setlocal
title Xbox Neighborhood Diagnostic

REM Self-contained launcher — copy this .cmd and diagnose-xbshlext.ps1 together to the target PC.
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\SysNative\WindowsPowerShell\v1.0\powershell.exe" (
  set "PS=%SystemRoot%\SysNative\WindowsPowerShell\v1.0\powershell.exe"
)

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0diagnose-xbshlext.ps1" -SaveReport %*
set "EC=%ERRORLEVEL%"

echo.
if not "%EC%"=="0" (
  echo Diagnostic finished with failures. See report on Desktop and in %%ProgramData%%\Xbox Neighborhood\Logs\
) else (
  echo Diagnostic finished. See report on Desktop and in %%ProgramData%%\Xbox Neighborhood\Logs\
)
echo.
pause
exit /b %EC%
