# Register the dev-staged xbshlext shell extension.
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$NoOpen,
    [switch]$Force,
    [switch]$Unregister,
    [switch]$ElevatedChild
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force
Assert-Administrator -AutoElevate -ScriptPath $PSCommandPath -BoundParameters $PSBoundParameters

try {
    if ($Unregister) {
        & (Join-Path $PSScriptRoot 'unregister-xbshlext-dev.ps1') -Force:$Force -ElevatedChild:$ElevatedChild
        return
    }

    Write-Host 'Stopping Explorer so staged binaries can be updated...'
    Stop-XbShellExtExplorer

    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    Sync-XbShellExtActiveSlotFromRegistry -RepoRoot $repoRoot

    $registered = Get-XbShellExtRegisteredPath
    if ($registered -and (Test-ProgramFilesPath $registered) -and -not $Force) {
        throw "Shell extension is registered from '$registered'. Run the uninstaller or pass -Force to replace with dev staging."
    }

    if ($registered -and (Test-Path -LiteralPath $registered)) {
        Write-Host "Unregistering previous registration: $registered"
        $prevDir = Split-Path -Parent $registered
        $prevComHost = Join-Path $prevDir 'Rxdk.XbShellExt.comhost.dll'
        if (Test-Path -LiteralPath $prevComHost) {
            Invoke-XbShellExtRegsvr32 -DllPath $prevComHost -Unregister
        }
        Clear-XbShellExtRegistry
        Stop-XbShellExtExplorer
        Start-Sleep -Seconds 2
    }

    & (Join-Path $PSScriptRoot 'fix-xbox-console-registry.ps1')

    & (Join-Path $PSScriptRoot 'stage-xbshlext-dev.ps1') -Configuration $Configuration -SkipBuild:$SkipBuild

    $comHost = Get-XbShellExtComHostPath -RepoRoot $repoRoot
    $shellDll = Get-XbShellExtShellDllPath -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $comHost)) {
        throw "Staged comhost not found: $comHost"
    }
    if (-not (Test-Path -LiteralPath $shellDll)) {
        throw "Staged native shell proxy not found: $shellDll"
    }

    Write-Host "Registering managed coclass: $comHost"
    Invoke-XbShellExtRegsvr32 -DllPath $comHost
    Repair-XbShellExtManagedRegistry -ModulePath $comHost

    Write-Host "Registering native shell proxy: $shellDll"
    Repair-XbShellExtRegistry -ModulePath $shellDll

    Enable-ExplorerNavPaneShowAllFolders

    $probe = Join-Path $PSScriptRoot 'ProbeXbCom\ProbeXbCom.csproj'
    if ((Test-Path -LiteralPath $probe) -and -not $NoOpen) {
        Write-Host 'Verifying COM activation...'
        dotnet run --project $probe -c Release 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'COM view probe failed. Xbox Neighborhood may not open until CreateViewObject succeeds.'
        }
    }

    Write-Host 'Restarting Explorer to load the new shell extension...'
    Stop-XbShellExtExplorer
    Start-XbShellExtExplorer

    $openCmd = Join-Path $repoRoot 'open-xbox-neighborhood.cmd'

    if (-not $NoOpen) {
        Write-Host 'Opening Xbox Neighborhood...'
        Open-XbShellExtNamespace
    }

    Write-Host "Open manually without admin: $openCmd"

    Write-Host 'Dev shell extension registered.'
}
catch {
    Write-XbShellExtScriptError -ErrorRecord $_ -ElevatedChild:$ElevatedChild
    exit 1
}
