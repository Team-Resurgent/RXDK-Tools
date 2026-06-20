# Clear stale Explorer nav-pane overrides and re-apply per-user namespace registration.
param(
    [switch]$ElevatedChild
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force
Assert-Administrator -AutoElevate -ScriptPath $PSCommandPath -BoundParameters $PSBoundParameters

try {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $shellDll = Get-XbShellExtShellDllPath -RepoRoot $repoRoot
    if (-not $shellDll) {
        throw 'Staged Rxdk.XbShellExt.Shell.dll not found. Run register-shell-ext.cmd first.'
    }

    Write-Host 'Repairing navigation pane registration...'
    Repair-XbShellExtNavPaneUserState -Clsid '{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}' -ModulePath $shellDll
    Enable-ExplorerNavPaneShowAllFolders

    Write-Host 'Restarting Explorer...'
    Stop-XbShellExtExplorer
    Start-XbShellExtExplorer

    Write-Host 'Navigation pane fix applied. Open a new File Explorer window and look under This PC or scroll the left pane for Xbox Neighborhood.'
}
catch {
    Write-XbShellExtScriptError -ErrorRecord $_ -ElevatedChild:$ElevatedChild
    exit 1
}
