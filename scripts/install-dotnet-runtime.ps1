# Installs the .NET 8 runtime required by RXDK managed tools.
# Prefers a bundled installer under <package>/runtime/ (offline). Falls back to Microsoft's dotnet-install script.
param(
    [string]$PackageRoot = '',
    [string]$RuntimeDir = '',
    [ValidateSet('windowsdesktop', 'dotnet')]
    [string]$Runtime = 'windowsdesktop',
    [string]$MajorVersion = '8',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Get-PackageRoot {
    param([string]$ScriptRoot, [string]$ExplicitRoot)

    if ($ExplicitRoot -and (Test-Path -LiteralPath $ExplicitRoot)) {
        return (Resolve-Path -LiteralPath $ExplicitRoot).Path
    }

    if (Test-Path -LiteralPath (Join-Path $ScriptRoot 'tools')) {
        return $ScriptRoot
    }

    $parent = Split-Path -Parent $ScriptRoot
    if (Test-Path -LiteralPath (Join-Path $parent 'tools')) {
        return $parent
    }

    return $ScriptRoot
}

function Test-DotNet8Installed {
    param(
        [ValidateSet('windowsdesktop', 'dotnet')]
        [string]$Kind,
        [string]$MajorVersion
    )

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $lines = & dotnet --list-runtimes 2>$null
        if ($Kind -eq 'windowsdesktop') {
            if ($lines | Where-Object { $_ -match "^Microsoft\.WindowsDesktop\.App $MajorVersion\." }) {
                return $true
            }
        }
        elseif ($lines | Where-Object { $_ -match "^Microsoft\.NETCore\.App $MajorVersion\." }) {
            return $true
        }
    }

    if ($Kind -eq 'windowsdesktop') {
        $sharedRoot = Join-Path ${env:ProgramFiles} 'dotnet\shared\Microsoft.WindowsDesktop.App'
        if (Test-Path -LiteralPath $sharedRoot) {
            $versions = @(Get-ChildItem -LiteralPath $sharedRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like "$MajorVersion.*" })
            if ($versions.Count -gt 0) {
                return $true
            }
        }
    }

    return $false
}

function Find-BundledWindowsInstaller {
    param([string]$Dir)

    if (-not (Test-Path -LiteralPath $Dir)) {
        return $null
    }

    $preferred = Join-Path $Dir 'windowsdesktop-runtime-win-x64.exe'
    if (Test-Path -LiteralPath $preferred) {
        return $preferred
    }

    return Get-ChildItem -LiteralPath $Dir -File -Filter 'windowsdesktop-runtime-*-win-x64.exe' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Install-FromBundledExe {
    param([string]$InstallerPath)

    Write-Host "Installing from bundled runtime: $InstallerPath"
    $args = @('/install', '/quiet', '/norestart')
    $process = Start-Process -FilePath $InstallerPath -ArgumentList $args -Wait -PassThru
    if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
        throw "Runtime installer failed with exit code $($process.ExitCode)."
    }
}

function Install-FromDotNetInstallScript {
    param(
        [ValidateSet('windowsdesktop', 'dotnet')]
        [string]$Kind,
        [string]$MajorVersion
    )

    $installScript = Join-Path ([System.IO.Path]::GetTempPath()) 'dotnet-install.ps1'
    Write-Host 'Downloading Microsoft dotnet-install.ps1...'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript -UseBasicParsing

    $installDir = Join-Path $env:USERPROFILE '.dotnet'
    Write-Host "Installing .NET $MajorVersion $Kind runtime to $installDir ..."
    & $installScript -Runtime $Kind -Channel $MajorVersion -InstallDir $installDir -Quality GA
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet-install.ps1 failed.'
    }

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($userPath -notlike "*$installDir*") {
        [Environment]::SetEnvironmentVariable('Path', "$userPath;$installDir", 'User')
        $env:Path = "$env:Path;$installDir"
        Write-Host "Added $installDir to the user PATH. Open a new terminal if tools still report a missing runtime."
    }
}

$packageRoot = Get-PackageRoot -ScriptRoot $PSScriptRoot -ExplicitRoot $PackageRoot
if (-not $RuntimeDir) {
    $RuntimeDir = Join-Path $packageRoot 'runtime'
}

Write-Host "RXDK Tools — .NET $MajorVersion runtime installer"
Write-Host "Package root: $packageRoot"

if (-not $Force -and (Test-DotNet8Installed -Kind $Runtime -MajorVersion $MajorVersion)) {
    Write-Host ".NET $MajorVersion runtime is already installed."
    exit 0
}

$bundled = Find-BundledWindowsInstaller -Dir $RuntimeDir
if ($bundled) {
    Install-FromBundledExe -InstallerPath $bundled
}
else {
    Write-Host "No bundled runtime found under: $RuntimeDir"
    Install-FromDotNetInstallScript -Kind $Runtime -MajorVersion $MajorVersion
}

if (-not (Test-DotNet8Installed -Kind $Runtime -MajorVersion $MajorVersion)) {
    throw ".NET $MajorVersion runtime was not detected after installation."
}

Write-Host 'Done.'
