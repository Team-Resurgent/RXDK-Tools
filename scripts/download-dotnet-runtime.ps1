# Downloads .NET 8 runtime installers into runtime/ for offline bundling with RXDK tool packages.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime,
    [string]$OutputDir = '',
    [string]$ChannelVersion = '8.0'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = if ($OutputDir) { $OutputDir } else { Join-Path $repoRoot 'runtime' }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$metadataUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/$ChannelVersion/releases.json"
Write-Host "Fetching release metadata from $metadataUrl ..."
$metadata = Invoke-RestMethod -Uri $metadataUrl

$latestVersion = $metadata.'latest-release'
if (-not $latestVersion) {
    throw 'Could not read latest-release from .NET release metadata.'
}

$release = $metadata.releases | Where-Object { $_.'release-version' -eq $latestVersion } | Select-Object -First 1
if (-not $release) {
    throw "Could not find release entry for version $latestVersion."
}

Write-Host "Latest stable release: $latestVersion"

function Get-ReleaseFile {
    param(
        [object]$Release,
        [string]$SectionName,
        [string]$Rid,
        [string]$NamePattern
    )

    $section = $Release.$SectionName
    if (-not $section -or -not $section.files) {
        return $null
    }

    return @($section.files | Where-Object {
            $_.rid -eq $Rid -and ($_.name -eq $NamePattern -or $_.name -like $NamePattern)
        }) | Select-Object -First 1
}

switch ($Runtime) {
    'win-x64' {
        $file = Get-ReleaseFile -Release $release -SectionName 'windowsdesktop' -Rid 'win-x64' -NamePattern 'windowsdesktop-runtime-win-x64.exe'
        if (-not $file) { throw 'Could not resolve Windows Desktop Runtime download URL.' }
        $dest = Join-Path $outDir 'windowsdesktop-runtime-win-x64.exe'
    }
    'linux-x64' {
        $file = Get-ReleaseFile -Release $release -SectionName 'runtime' -Rid 'linux-x64' -NamePattern 'dotnet-runtime-linux-x64.tar.gz'
        if (-not $file) { throw 'Could not resolve Linux x64 runtime download URL.' }
        $dest = Join-Path $outDir 'dotnet-runtime-linux-x64.tar.gz'
    }
    'linux-arm64' {
        $file = Get-ReleaseFile -Release $release -SectionName 'runtime' -Rid 'linux-arm64' -NamePattern 'dotnet-runtime-linux-arm64.tar.gz'
        if (-not $file) { throw 'Could not resolve Linux arm64 runtime download URL.' }
        $dest = Join-Path $outDir 'dotnet-runtime-linux-arm64.tar.gz'
    }
    'osx-x64' {
        $file = Get-ReleaseFile -Release $release -SectionName 'runtime' -Rid 'osx-x64' -NamePattern 'dotnet-runtime-osx-x64.tar.gz'
        if (-not $file) { throw 'Could not resolve macOS x64 runtime download URL.' }
        $dest = Join-Path $outDir 'dotnet-runtime-osx-x64.tar.gz'
    }
    'osx-arm64' {
        $file = Get-ReleaseFile -Release $release -SectionName 'runtime' -Rid 'osx-arm64' -NamePattern 'dotnet-runtime-osx-arm64.tar.gz'
        if (-not $file) { throw 'Could not resolve macOS arm64 runtime download URL.' }
        $dest = Join-Path $outDir 'dotnet-runtime-osx-arm64.tar.gz'
    }
}

Write-Host "Downloading $($file.name) ..."
Invoke-WebRequest -Uri $file.url -OutFile $dest -UseBasicParsing
Write-Host "Saved: $dest ($([math]::Round((Get-Item -LiteralPath $dest).Length / 1MB, 1)) MB)"
