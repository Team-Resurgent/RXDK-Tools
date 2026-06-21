# Register the production Xbox Neighborhood shell extension from an install directory.
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
    throw 'XbShellExtDev.psm1 was not found next to install-xbshlext.ps1.'
}

Import-Module $modulePath -Force

$installDir = (Resolve-Path -LiteralPath $InstallDir).Path
$comHost = Join-Path $installDir 'Rxdk.XbShellExt.comhost.dll'
$shellDll = Join-Path $installDir 'Rxdk.XbShellExt.Shell.dll'
$managedDll = Join-Path $installDir 'Rxdk.XbShellExt.dll'

foreach ($required in @($comHost, $shellDll, $managedDll)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing required install file: $required"
    }
}

if (-not $SkipExplorerRestart) {
    Stop-XbShellExtExplorer
}

Write-Host "Registering namespace shell extension: $shellDll"
Invoke-XbShellExtRegsvr32 -DllPath $shellDll
Repair-XbShellExtRegistry -ModulePath $shellDll

Write-Host "Registering managed coclass: $comHost"
Invoke-XbShellExtRegsvr32 -DllPath $comHost
Repair-XbShellExtManagedRegistry -ModulePath $comHost
Enable-ExplorerNavPaneShowAllFolders

if (-not $SkipExplorerRestart) {
    Start-XbShellExtExplorer
}

Write-Host 'Shell extension registration complete.'
