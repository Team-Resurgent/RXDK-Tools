# Remove Xbox Neighborhood shell extension registration for an install directory.
param(
    [Parameter(Mandatory)]
    [string]$InstallDir,
    [switch]$SkipExplorerRestart
)

$ErrorActionPreference = 'Stop'

$moduleCandidates = @(
    (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1')
    (Join-Path $PSScriptRoot 'XbShellExtDev.psm1')
)
$modulePath = $moduleCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $modulePath) {
    throw 'XbShellExtDev.psm1 was not found next to uninstall-xbshlext.ps1.'
}

Import-Module $modulePath -Force

if (-not (Test-Path -LiteralPath $InstallDir)) {
    Write-Host "Install directory not found: $InstallDir"
    Write-Host 'Removing shell extension registry keys...'
    Clear-XbShellExtRegistration
    return
}

$installDir = (Resolve-Path -LiteralPath $InstallDir).Path
$comHost = Join-Path $installDir 'Rxdk.XbShellExt.comhost.dll'
$legacyDll = Join-Path $installDir 'xbshlext.dll'

if (-not $SkipExplorerRestart) {
    Stop-XbShellExtExplorer
}

if (Test-Path -LiteralPath $comHost) {
    Write-Host "Unregistering managed coclass: $comHost"
    Invoke-XbShellExtRegsvr32 -DllPath $comHost -Unregister
}
elseif (Test-Path -LiteralPath $legacyDll) {
    Write-Host "Unregistering legacy shell extension: $legacyDll"
    Invoke-XbShellExtRegsvr32 -DllPath $legacyDll -Unregister
}

Write-Host 'Removing shell extension registry keys...'
Clear-XbShellExtRegistration

if (-not $SkipExplorerRestart) {
    Start-XbShellExtExplorer
}

Write-Host 'Shell extension registration removed.'
