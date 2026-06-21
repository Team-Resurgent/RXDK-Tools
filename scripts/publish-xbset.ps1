# Publish single-file self-contained xbset (or use publish-managed-cli-tools.ps1 for the full bundle)
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src-dotnet\Rxdk.XbSet\Rxdk.XbSet.csproj"
$publishDir = if ($OutputDir) { $OutputDir } else { Join-Path $repoRoot "out\publish\xbset-$Runtime" }

Write-Host "Publishing xbset (single-file, $Runtime)..."
dotnet publish $project -c Release -r $Runtime -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to: $publishDir"
