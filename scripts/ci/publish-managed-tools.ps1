# Publish xbset and Rxdk.XbWatson for CI / local staging.

param(

    [string]$Runtime = "win-x64",

    [string]$ArtifactDir = "artifacts/managed"

)



$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

$outDir = Join-Path $repoRoot (Join-Path $ArtifactDir $Runtime)



if (Test-Path -LiteralPath $outDir) {

    Remove-Item -LiteralPath $outDir -Recurse -Force

}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null



$publishScript = Join-Path $repoRoot 'scripts\publish-xbset.ps1'

$watsonScript = Join-Path $repoRoot 'scripts\publish-xbwatson.ps1'



& $publishScript -Runtime $Runtime -OutputDir (Join-Path $outDir 'xbset')

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }



& $watsonScript -Runtime $Runtime -OutputDir (Join-Path $outDir 'xbwatson')

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem -Path $outDir -Recurse -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Managed tools staged in $outDir"

