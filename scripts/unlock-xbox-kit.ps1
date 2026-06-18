# Unlock an Xbox dev kit left locked by parity security tests.
#
# Examples:
#   .\scripts\unlock-xbox-kit.ps1 -Console myxbox
#   .\scripts\unlock-xbox-kit.ps1 -Console myxbox -Password test
#
param(
    [string]$Console = $env:RXDK_TEST_CONSOLE,
    [string]$Password = $env:RXDK_TEST_PASSWORD,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testsProject = Join-Path $repoRoot 'src-dotnet\Rxdk.Xbdm.Tests\Rxdk.Xbdm.Tests.csproj'
$testsDll = Join-Path $repoRoot "src-dotnet\Rxdk.Xbdm.Tests\bin\$Configuration\net8.0\Rxdk.Xbdm.Tests.dll"

if ([string]::IsNullOrWhiteSpace($Console)) {
    $Console = Read-Host 'Xbox console name or IP (RXDK_TEST_CONSOLE)'
}

if ([string]::IsNullOrWhiteSpace($Console)) {
    throw 'Console name or IP is required. Pass -Console or set RXDK_TEST_CONSOLE.'
}

Write-Host "Building unlock helper..."
dotnet build $testsProject -c $Configuration --no-restore 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    dotnet build $testsProject -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$env:RXDK_TEST_CONSOLE = $Console
if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $env:RXDK_TEST_PASSWORD = $Password
}

$args = @('--unlock', $Console)
if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $args += $Password
}

Write-Host "Unlocking kit '$Console'..."
dotnet $testsDll @args
exit $LASTEXITCODE
