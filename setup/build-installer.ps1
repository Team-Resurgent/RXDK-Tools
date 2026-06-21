# Build XboxNeighborhood-Setup.exe with Inno Setup 6.
param(
    [string]$IssPath = (Join-Path $PSScriptRoot 'setup.iss')
)
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$SetupDir = $PSScriptRoot

# Inno Setup /D values must stay repo-relative (forward slashes). Absolute paths like
# D:\a\... from GitHub Actions break ISCC parsing (\D: is treated as an escape prefix).
$InnoDefaultOutputDir = '../out/bin/x64/Release'
$InnoAltOutputDir = '../out/bin/x64/Release-alt'

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

$shellExtProject = Join-Path $RepoRoot 'src\Rxdk.XbShellExt\Rxdk.XbShellExt.csproj'
$shellProxyProject = Join-Path $RepoRoot 'src\Rxdk.XbShellExt.Shell\Rxdk.XbShellExt.Shell.vcxproj'

Write-Host "Building Rxdk.XbShellExt (Release|win-x64)..." -ForegroundColor Cyan
dotnet build $shellExtProject -c Release -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet build failed for Rxdk.XbShellExt'
}

function Resolve-MSBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw 'vswhere.exe not found. Install Visual Studio with the Desktop development with C++ workload.'
    }

    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path -LiteralPath $msbuild)) {
        throw 'MSBuild.exe not found. Install Visual Studio with the Desktop development with C++ workload.'
    }

    return $msbuild
}

Write-Host 'Building Rxdk.XbShellExt.Shell (Release|x64)...' -ForegroundColor Cyan
$msbuild = Resolve-MSBuildPath
& $msbuild $shellProxyProject /p:Configuration=Release /p:Platform=x64 /v:m
if ($LASTEXITCODE -ne 0) {
    throw 'msbuild failed for Rxdk.XbShellExt.Shell'
}

$buildOut = Join-Path $RepoRoot 'src\Rxdk.XbShellExt\bin\Release\net8.0-windows\win-x64'
if (-not (Test-Path -LiteralPath $buildOut)) {
    throw "Missing build output: $buildOut"
}

$managedFiles = @(
    'Rxdk.XbShellExt.comhost.dll',
    'Rxdk.XbShellExt.dll',
    'Rxdk.XbShellExt.UI.dll',
    'Rxdk.XbNeighborhood.Core.dll',
    'Rxdk.KitConfig.dll',
    'Rxdk.Xbdm.KitServices.dll',
    'Rxdk.Xbdm.Managed.dll',
    'Rxdk.Xbdm.Abstractions.dll',
    'Rxdk.XbShellExt.deps.json',
    'Rxdk.XbShellExt.runtimeconfig.json'
)
foreach ($name in $managedFiles) {
    $source = Join-Path $buildOut $name
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Missing managed build output: $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $installDir $name) -Force
}

$shellProxyOut = Join-Path $RepoRoot 'out\bin\x64\Release\Rxdk.XbShellExt.Shell.dll'
$shellProxyDest = Join-Path $installDir 'Rxdk.XbShellExt.Shell.dll'
if (-not (Test-Path -LiteralPath $shellProxyOut)) {
    throw "Missing native shell proxy build output: $shellProxyOut"
}
if ($shellProxyOut -ne $shellProxyDest) {
    Copy-Item -LiteralPath $shellProxyOut -Destination $shellProxyDest -Force
}

Copy-Item -LiteralPath (Join-Path $RepoRoot 'assets\console.ico') -Destination (Join-Path $installDir 'console.ico') -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot 'assets\xbox-light.ico') -Destination (Join-Path $installDir 'xbox.ico') -Force

$requiredStaged = @(
    'Rxdk.XbShellExt.Shell.dll',
    'Rxdk.XbShellExt.comhost.dll',
    'Rxdk.XbShellExt.dll',
    'Rxdk.XbShellExt.UI.dll',
    'Rxdk.XbNeighborhood.Core.dll',
    'Rxdk.KitConfig.dll',
    'Rxdk.Xbdm.KitServices.dll',
    'Rxdk.Xbdm.Managed.dll',
    'Rxdk.Xbdm.Abstractions.dll',
    'Rxdk.XbShellExt.runtimeconfig.json',
    'console.ico',
    'xbox.ico'
)
foreach ($name in $requiredStaged) {
    $path = Join-Path $installDir $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing staged installer payload: $path"
    }
}

function Copy-InnoWizardBitmap {
    param(
        [string]$Source,
        [string]$Destination,
        [int]$Width,
        [int]$Height
    )

    Add-Type -AssemblyName System.Drawing
    $src = [System.Drawing.Image]::FromFile($Source)
    $bmp = $null
    try {
        if (($src.Width -eq $Width) -and ($src.Height -eq $Height)) {
            Copy-Item -LiteralPath $Source -Destination $Destination -Force
            return
        }

        $bmp = New-Object System.Drawing.Bitmap $Width, $Height
        $graphics = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($src, 0, 0, $Width, $Height)
        }
        finally {
            $graphics.Dispose()
        }

        $bmp.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $src.Dispose()
        if ($null -ne $bmp) { $bmp.Dispose() }
    }
}

Write-Host 'Staging Inno Setup wizard bitmaps...' -ForegroundColor Cyan
Copy-InnoWizardBitmap `
    -Source (Join-Path $RepoRoot 'assets\xwmark.bmp') `
    -Destination (Join-Path $PSScriptRoot 'WizardImage.bmp') `
    -Width 164 -Height 314
Copy-InnoWizardBitmap `
    -Source (Join-Path $RepoRoot 'assets\xheader.bmp') `
    -Destination (Join-Path $PSScriptRoot 'WizardSmallImage.bmp') `
    -Width 55 -Height 55

foreach ($required in @('Icon.ico', 'WizardImage.bmp', 'WizardSmallImage.bmp')) {
    $path = Join-Path $PSScriptRoot $required
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing $path"
    }
}

function Test-FileIsLocked {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $stream.Close()
        return $false
    }
    catch {
        return $true
    }
}

$iscc = Ensure-Iscc

$defaultSetupExe = Join-Path $installDir 'XboxNeighborhood-Setup.exe'
$installerOutputDir = $installDir
$installerOutputBaseName = 'XboxNeighborhood-Setup'
$innoOutputDirDefine = $null

if (Test-FileIsLocked $defaultSetupExe) {
    $installerOutputDir = Join-Path $RepoRoot 'out\bin\x64\Release-alt'
    $innoOutputDirDefine = $InnoAltOutputDir
    New-Item -ItemType Directory -Force -Path $installerOutputDir | Out-Null
    Write-Warning "Existing installer is locked: $defaultSetupExe"
    Write-Warning "Close XboxNeighborhood-Setup.exe if it is running. Building to: $installerOutputDir"
}
elseif (Test-Path -LiteralPath $defaultSetupExe) {
    Remove-Item -LiteralPath $defaultSetupExe -Force -ErrorAction SilentlyContinue
}

Write-Host "Building installer with $iscc" -ForegroundColor Cyan
$isccArgs = @("/DInstallerOutputBaseName=$installerOutputBaseName")
if ($innoOutputDirDefine) {
    $isccArgs = @("/DInstallerOutputDir=$innoOutputDirDefine") + $isccArgs
}
Push-Location -LiteralPath $SetupDir
try {
    & $iscc @isccArgs 'setup.iss'
}
finally {
    Pop-Location
}
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed (exit $LASTEXITCODE)"
}

$output = Join-Path $installerOutputDir "$installerOutputBaseName.exe"
if ($installerOutputDir -ne $installDir -and (Test-Path -LiteralPath $output)) {
    try {
        Copy-Item -LiteralPath $output -Destination $defaultSetupExe -Force
        Remove-Item -LiteralPath $output -Force
        $output = $defaultSetupExe
    }
    catch {
        Write-Warning "Built installer at $output but could not replace locked $defaultSetupExe"
    }
}

Write-Host "Installer: $output" -ForegroundColor Green
