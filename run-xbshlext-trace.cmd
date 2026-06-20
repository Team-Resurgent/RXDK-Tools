@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-xbshlext-trace.ps1" -RestartExplorer %*
pause
