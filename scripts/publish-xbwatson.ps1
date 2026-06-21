# Publish single-file self-contained xbWatson
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\Rxdk.XbWatson\Rxdk.XbWatson.csproj"
$publishDir = if ($OutputDir) { $OutputDir } else { Join-Path $repoRoot "out\publish\xbwatson-$Runtime" }

Write-Host "Publishing xbWatson (single-file, $Runtime)..."
dotnet publish $project -c Release -r $Runtime -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to: $publishDir"
