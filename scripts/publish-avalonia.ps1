# Build and publish the Avalonia RXDK Neighborhood app for Windows x64.
param(
    [ValidateSet("framework", "self-contained")]
    [string]$Mode = "framework"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "RXDKNeighborhood.sln"))) {
    $repoRoot = $PSScriptRoot
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

Write-Host ""
Write-Host "Published to: $publishDir"
Write-Host "Run: $(Join-Path $publishDir 'RXDKNeighborhood.exe')"
