# Build RXDKNeighborhood.sln (Release|x64) and stage installer + managed tool artifacts for CI.
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [string]$Solution = 'RXDKNeighborhood.sln'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

function Find-MsBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw 'vswhere.exe not found. Install Visual Studio with the Desktop development with C++ workload.'
    }

    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1
    if (-not $msbuild) {
        throw 'MSBuild not found. Install Visual Studio with the Desktop development with C++ workload.'
    }
    return $msbuild
}

function Find-Iscc {
    foreach ($candidate in @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Ensure-InnoSetup {
    if (Find-Iscc) { return }

    Write-Host 'Inno Setup 6 not found. Installing with Chocolatey...'
    $chocoCmd = Get-Command choco -ErrorAction SilentlyContinue
    if (-not $chocoCmd) {
        throw 'Inno Setup 6 is required for XboxNeighborhood-Setup.exe. Install Inno Setup or Chocolatey on the runner.'
    }
    $choco = $chocoCmd.Source

    & $choco install innosetup -y --no-progress
    if ($LASTEXITCODE -ne 0) {
        throw "choco install innosetup failed (exit $LASTEXITCODE)"
    }

    if (-not (Find-Iscc)) {
        throw 'Inno Setup was installed but ISCC.exe was not found.'
    }
}

$msbuild = Find-MsBuild
Ensure-InnoSetup

Write-Host "MSBuild: $msbuild"
Write-Host "Building $Solution ($Configuration|$Platform)..."

& $msbuild (Join-Path $repoRoot $Solution) `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    /m `
    /v:m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$binDir = Join-Path $repoRoot "out\bin\$Platform\$Configuration"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

Write-Host ''
Write-Host 'Building Xbox Neighborhood installer...'
& (Join-Path $repoRoot 'setup\build-installer.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$installerSrc = Join-Path $binDir 'XboxNeighborhood-Setup.exe'
if (-not (Test-Path -LiteralPath $installerSrc)) {
    throw "Installer not found: $installerSrc"
}

$installerDir = Join-Path $repoRoot 'artifacts/installer'
if (Test-Path -LiteralPath $installerDir) {
    Remove-Item -LiteralPath $installerDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
Copy-Item -LiteralPath $installerSrc -Destination $installerDir -Force

Write-Host ''
Write-Host 'Publishing managed tools (xbset, xbWatson)...'
& (Join-Path $repoRoot 'scripts\ci\publish-managed-tools.ps1') -Runtime win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
