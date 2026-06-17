# Build and publish the Avalonia RXDK Neighborhood app for Windows x64.
param(
    [ValidateSet("framework", "self-contained")]
    [string]$Mode = "framework",
    [switch]$SkipNative
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "RXDKNeighborhood.sln"))) {
    $repoRoot = $PSScriptRoot
}

$xbdmSrc = Join-Path $repoRoot "out\bin\x64\Release\xbdm.dll"

if (-not $SkipNative) {
    $msbuild = $null
    $vswhere = Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }

    if (-not $msbuild) {
        $candidates = @(
            "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
        )
        $msbuild = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }

    if (-not $msbuild) {
        if (Test-Path $xbdmSrc) {
            Write-Warning "MSBuild not found. Using existing $xbdmSrc"
        } else {
            throw "MSBuild not found and xbdm.dll is missing. Build src/xbdm-native first or pass -SkipNative after building native."
        }
    } else {
        Write-Host "Building xbdm.dll..."
        & $msbuild (Join-Path $repoRoot "src\xbdm-native\xbdm-native.vcxproj") /p:Configuration=Release /p:Platform=x64 /v:m
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

$project = Join-Path $repoRoot "src-dotnet\RXDKNeighborhood\RXDKNeighborhood.csproj"
$publishDir = Join-Path $repoRoot "out\publish\RXDKNeighborhood-win-x64"

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $publishDir
)

if ($Mode -eq "self-contained") {
    $publishArgs += @("--self-contained", "true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true")
} else {
    $publishArgs += @("--self-contained", "false")
}

Write-Host "Publishing Avalonia app ($Mode)..."
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Test-Path $xbdmSrc) {
    Copy-Item $xbdmSrc (Join-Path $publishDir "xbdm.dll") -Force
} else {
    Write-Warning "xbdm.dll not found at $xbdmSrc"
}

Write-Host ""
Write-Host "Published to: $publishDir"
Write-Host "Run: $(Join-Path $publishDir 'RXDKNeighborhood.exe')"
