# Publishes managed CLI tools and stages a release folder with tools/, runtime/, and install scripts.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime,
    [string]$OutputDir = '',
    [switch]$SkipPublish,
    [switch]$SkipRuntimeDownload
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$packageDir = if ($OutputDir) {
    $OutputDir
} else {
    Join-Path $repoRoot "out/publish/managed/$Runtime"
}

$toolsDir = Join-Path $packageDir 'tools'
$runtimeDir = Join-Path $packageDir 'runtime'

New-Item -ItemType Directory -Force -Path $toolsDir, $runtimeDir | Out-Null

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-managed-cli-tools.ps1') -Runtime $Runtime -OutputDir $toolsDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipRuntimeDownload) {
    & (Join-Path $PSScriptRoot 'download-dotnet-runtime.ps1') -Runtime $Runtime -OutputDir $runtimeDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-dotnet-runtime.ps1') -Destination (Join-Path $packageDir 'install-dotnet-runtime.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-dotnet-runtime.cmd') -Destination (Join-Path $packageDir 'install-dotnet-runtime.cmd') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-dotnet-runtime.sh') -Destination (Join-Path $packageDir 'install-dotnet-runtime.sh') -Force

Write-Host "Staged package: $packageDir"
Write-Host "  tools/    — single-file executables"
Write-Host "  runtime/  — bundled .NET 8 runtime installer"
Write-Host "  install-dotnet-runtime.* — run before first use if .NET 8 is not installed"
