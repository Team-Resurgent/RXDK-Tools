@echo off

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\unregister-xbshlext-dev.ps1" %*

set EXITCODE=%ERRORLEVEL%

if %EXITCODE% neq 0 pause

exit /b %EXITCODE%

