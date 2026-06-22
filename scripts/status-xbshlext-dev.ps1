# Show xbshlext registration and dev staging status.
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force

$registered = Get-XbShellExtRegisteredPath
$stageDir = Get-XbShellExtDefaultStageDir -RepoRoot $repoRoot
$stageRoot = Join-Path $repoRoot 'out\dev\xbshlext'
$activeSlot = if (Test-Path -LiteralPath (Join-Path $stageRoot 'active.slot')) {
    (Get-Content -LiteralPath (Join-Path $stageRoot 'active.slot') -Raw).Trim()
} else {
    '(unknown)'
}

Write-Host 'Xbox Neighborhood shell extension status'
Write-Host "  CLSID: {DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}"
Write-Host "  Registered: $(if ($registered) { $registered } else { '(none)' })"
Write-Host "  Active stage slot: $activeSlot"
Write-Host "  Staging dir: $stageDir"

$stagedPath = Get-XbShellExtComHostPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $stagedPath)) {
    $stagedPath = $null
}

Write-Host "  Staged module: $(if ($stagedPath) { $stagedPath } else { '(missing — run stage-xbshlext-dev.ps1)' })"

if ($registered -and $stagedPath) {
    $same = [string]::Equals(
        (Resolve-Path -LiteralPath $registered).Path,
        (Resolve-Path -LiteralPath $stagedPath).Path,
        [StringComparison]::OrdinalIgnoreCase)
    Write-Host "  Matches staging: $same"
}

Write-Host "  Elevated: $(Test-Administrator)"
