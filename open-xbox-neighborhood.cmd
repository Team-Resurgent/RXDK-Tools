@echo off
setlocal

set "SHELL_DLL=%~dp0out\dev\xbshlext\slot-a\Rxdk.XbShellExt.Shell.dll"
if not exist "%SHELL_DLL%" set "SHELL_DLL=%~dp0out\dev\xbshlext\slot-b\Rxdk.XbShellExt.Shell.dll"
if not exist "%SHELL_DLL%" (
  echo Could not find staged Rxdk.XbShellExt.Shell.dll. Run register-shell-ext.cmd first.
  pause
  exit /b 1
)

REM Must not run as admin.
net session >nul 2>&1
if %errorlevel%==0 (
  echo Do not run this script as administrator.
  pause
  exit /b 1
)

REM Host the namespace view directly (bypasses Explorer frame hang on Win11).
"%SystemRoot%\System32\rundll32.exe" "%SHELL_DLL%",OpenNamespace
