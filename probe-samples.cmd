@echo off
setlocal

REM Optional: set XB_TRANSFER_DEBUG_DELAY=3 to pause on each file at 100%% for visual checks.
set "REPO=%~dp0"
dotnet run --project "%REPO%scripts\ProbeFolder\ProbeFolder.csproj" -c Release -- %*