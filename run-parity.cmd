@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-xbdm-parity.ps1" %*
exit /b %ERRORLEVEL%
