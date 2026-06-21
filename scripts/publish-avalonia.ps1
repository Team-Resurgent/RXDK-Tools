# Build and publish the Avalonia RXDK Neighborhood app.
param(
    [ValidateSet("framework", "self-contained")]
    [string]$Mode = "framework",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "RXDKTools.sln"))) {
    $repoRoot = $PSScriptRoot
}

$project = Join-Path $repoRoot "src\Rxdk.XbNeighborhood\Rxdk.XbNeighborhood.csproj"
$publishDir = Join-Path $repoRoot "out\publish\Rxdk.XbNeighborhood-$Runtime"

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $publishDir
)

if ($Mode -eq "self-contained") {
    $publishArgs += @("-p:SelfContained=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:IncludeAllContentForSelfExtract=true")
} else {
    $publishArgs += @("--self-contained", "false")
}

Write-Host "Publishing Avalonia Rxdk.XbNeighborhood ($Mode, $Runtime)..."
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Published to: $publishDir"
