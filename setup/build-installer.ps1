# Build XboxNeighborhood-Setup.exe with Inno Setup 6.
param(
    [string]$IssPath = (Join-Path $PSScriptRoot 'setup.iss')
)
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Find-Iscc {
    foreach ($candidate in @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Get-ToolPath {
    param([string]$Name)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Install-InnoSetup {
    Write-Host 'Inno Setup 6 not found. Installing...' -ForegroundColor Yellow

    $winget = Get-ToolPath 'winget'
    if ($winget) {
        Write-Host "Installing via winget ($winget)..." -ForegroundColor Cyan
        & $winget @(
            'install',
            '--id', 'JRSoftware.InnoSetup',
            '--exact',
            '--scope', 'machine',
            '--accept-package-agreements',
            '--accept-source-agreements',
            '--disable-interactivity',
            '--silent'
        )
        if ($LASTEXITCODE -ne 0) {
            throw @"
winget install JRSoftware.InnoSetup failed (exit $LASTEXITCODE).
Run this script from an elevated shell, or install Inno Setup 6 from https://jrsoftware.org/isinfo.php
"@
        }
        return
    }

    $choco = Get-ToolPath 'choco'
    if ($choco) {
        Write-Host "Installing via Chocolatey ($choco)..." -ForegroundColor Cyan
        & $choco install innosetup -y --no-progress
        if ($LASTEXITCODE -ne 0) {
            throw @"
choco install innosetup failed (exit $LASTEXITCODE).
Run this script from an elevated shell, or install Inno Setup 6 from https://jrsoftware.org/isinfo.php
"@
        }
        return
    }

    throw @'
Inno Setup 6 is not installed and could not be installed automatically.
Install Inno Setup 6 from https://jrsoftware.org/isinfo.php, or install winget/Chocolatey and rerun.
'@
}

function Ensure-Iscc {
    $iscc = Find-Iscc
    if ($iscc) { return $iscc }

    Install-InnoSetup
    $iscc = Find-Iscc
    if (-not $iscc) {
        throw 'Inno Setup 6 was installed but ISCC.exe was not found. Reopen the terminal and try again.'
    }
    return $iscc
}

if (-not (Test-Path -LiteralPath $IssPath)) {
    throw "Missing $IssPath"
}

$installDir = Join-Path $RepoRoot 'out\bin\x64\Release'
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

$shellExtProject = Join-Path $RepoRoot 'src-dotnet\Rxdk.XbShellExt\Rxdk.XbShellExt.csproj'
Write-Host "Building Rxdk.XbShellExt (Release|win-x64)..." -ForegroundColor Cyan
dotnet build $shellExtProject -c Release -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet build failed for Rxdk.XbShellExt'
}

$buildOut = Join-Path $RepoRoot 'src-dotnet\Rxdk.XbShellExt\bin\Release\net8.0-windows\win-x64'
if (-not (Test-Path -LiteralPath $buildOut)) {
    throw "Missing build output: $buildOut"
}

Get-ChildItem -LiteralPath $buildOut -File | Copy-Item -Destination $installDir -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot 'assets\shell\console.ico') -Destination (Join-Path $installDir 'console.ico') -Force

$comHost = Join-Path $installDir 'Rxdk.XbShellExt.comhost.dll'
if (-not (Test-Path -LiteralPath $comHost)) {
    throw "Missing $comHost after staging managed shell extension build"
}

foreach ($required in @('Icon.ico', 'WizardImage.bmp', 'WizardSmallImage.bmp')) {
    $path = Join-Path $PSScriptRoot $required
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing $path"
    }
}

$iscc = Ensure-Iscc

Write-Host "Building installer with $iscc" -ForegroundColor Cyan
& $iscc $IssPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed (exit $LASTEXITCODE)"
}

$output = Join-Path $installDir 'XboxNeighborhood-Setup.exe'
Write-Host "Installer: $output" -ForegroundColor Green
