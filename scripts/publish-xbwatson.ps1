# Publish single-file framework-dependent xbWatson into the managed tools bundle.
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsDir = if ($OutputDir) { $OutputDir } else { Join-Path $repoRoot "out\publish\managed-cli-tools-$Runtime" }
$project = Join-Path $repoRoot "src\Rxdk.XbWatson\Rxdk.XbWatson.csproj"
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "rxdk-xbwatson-publish-$Runtime-$(Get-Random)"

New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Publishing xbWatson (single-file framework-dependent, $Runtime)..."
    dotnet publish $project -c Release -r $Runtime -o $staging
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $publishedExe = Get-ChildItem -Path $staging -File |
        Where-Object { $_.BaseName -eq "xbwatson" } |
        Select-Object -First 1
    if ($null -eq $publishedExe) {
        $names = (Get-ChildItem -Path $staging -File | ForEach-Object { $_.Name }) -join ", "
        throw "Expected published executable 'xbwatson', found: $names"
    }

    $source = $publishedExe.FullName
    $destination = Join-Path $toolsDir $publishedExe.Name
    Copy-Item -LiteralPath $source -Destination $destination -Force
    Write-Host "Published to: $destination"
}
finally {
    if (Test-Path $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
