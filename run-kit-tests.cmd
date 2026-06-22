@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-xbdm-kit-tests.ps1" %*
exit /b %ERRORLEVEL%
