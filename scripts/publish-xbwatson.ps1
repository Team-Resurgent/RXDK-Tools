# Publish cross-platform Rxdk.XbWatson
param(
    [ValidateSet("framework", "self-contained")]
    [string]$Mode = "framework",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src-dotnet\Rxdk.XbWatson\Rxdk.XbWatson.csproj"
$publishDir = Join-Path $repoRoot "out\publish\Rxdk.XbWatson-$Runtime"

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $publishDir
)

if ($Mode -eq "self-contained") {
    $publishArgs += @("--self-contained", "true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true")
} else {
    $publishArgs += @("--self-contained", "false")
}

Write-Host "Publishing Rxdk.XbWatson ($Mode, $Runtime)..."
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to: $publishDir"
