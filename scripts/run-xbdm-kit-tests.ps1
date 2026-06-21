# Run managed XBDM hardware tests and write artifacts/xbdm-kit-report.md
#
# Examples:
#   .\scripts\run-xbdm-kit-tests.ps1 -Console MyXbox
#   .\scripts\run-xbdm-kit-tests.ps1 -Console 192.168.1.50 -Password secret -Full
#   .\scripts\run-xbdm-kit-tests.ps1 -Console MyXbox -Quick
#
param(
    [string]$Console = $env:RXDK_TEST_CONSOLE,
    [string]$Password = $env:RXDK_TEST_PASSWORD,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Quick,
    [switch]$Full,
    [switch]$OpenReport,
    [switch]$Pause,
    [string]$ReportPath = $(if ($env:RXDK_KIT_REPORT) { $env:RXDK_KIT_REPORT } elseif ($env:RXDK_PARITY_REPORT) { $env:RXDK_PARITY_REPORT } else { $null })
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testsProject = Join-Path $repoRoot 'src-dotnet\Rxdk.Xbdm.Tests\Rxdk.Xbdm.Tests.csproj'
$bridgeProject = Join-Path $repoRoot 'src-dotnet\Rxdk.XboxDbgBridge.Cli\Rxdk.XboxDbgBridge.Cli.csproj'
$artifactsDir = Join-Path $repoRoot 'artifacts'

if ([string]::IsNullOrWhiteSpace($Console)) {
    $Console = Read-Host 'Xbox console name or IP (RXDK_TEST_CONSOLE)'
}

if ([string]::IsNullOrWhiteSpace($Console)) {
    throw 'Console name or IP is required. Pass -Console or set RXDK_TEST_CONSOLE.'
}

if (-not $Quick -and -not $PSBoundParameters.ContainsKey('Full')) {
    $Full = $true
}

if ($Quick) {
    $Full = $false
}

if ($Full -and [string]::IsNullOrWhiteSpace($Password)) {
    $secure = Read-Host 'XBDM password (leave blank if none)' -AsSecureString
    if ($secure.Length -gt 0) {
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try {
            $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
        }
    }
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $artifactsDir 'xbdm-kit-report.md'
}

Write-Host ''
Write-Host '=== XBDM managed hardware test ===' -ForegroundColor Cyan
Write-Host "Console : $Console"
Write-Host "Mode    : $(if ($Full) { 'Full (launch + bridge + reboot + security)' } else { 'Quick (read-only / file ops)' })"
Write-Host "Report  : $ReportPath"
Write-Host ''

Set-Location $repoRoot

Write-Host 'Building hardware tests...' -ForegroundColor DarkGray
dotnet build $bridgeProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Bridge build failed.'
}

Write-Host 'Building test project...' -ForegroundColor DarkGray
dotnet build $testsProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Test project build failed.'
}

$env:RXDK_TEST_CONSOLE = $Console
$env:RXDK_KIT_REPORT = $ReportPath

if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $env:RXDK_TEST_PASSWORD = $Password
}
else {
    Remove-Item Env:RXDK_TEST_PASSWORD -ErrorAction SilentlyContinue
}

Remove-Item Env:RXDK_KIT_ALLOW_EXEC -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_KIT_ALLOW_LAUNCH -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_KIT_ALLOW_BRIDGE -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_KIT_ALLOW_REBOOT -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_KIT_ALLOW_SECURITY -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_KIT_NO_RESTORE -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_KIT_PAUSE -ErrorAction SilentlyContinue

if ($Pause) {
    $env:RXDK_KIT_PAUSE = '1'
}

if ($Full) {
    $env:RXDK_KIT_ALLOW_EXEC = '1'
    $env:RXDK_KIT_ALLOW_BRIDGE = '1'
    $env:RXDK_KIT_ALLOW_REBOOT = '1'
    $env:RXDK_KIT_ALLOW_SECURITY = '1'
    Write-Host 'Progress lines stream live as [HH:mm:ss] (direct console, no VSTest).' -ForegroundColor DarkGray
    Write-Host 'Full mode: Execution + bridge first; Security locks/unlocks the kit at the end.' -ForegroundColor Yellow
    if ($Pause) {
        Write-Host 'Pause mode: bridge 5s segments; Security sets admin password test (Manage…) for Neighborhood verification.' -ForegroundColor Yellow
    }
    else {
        Write-Host 'Security suite locks the kit via PC user manage, then restores unlocked. Leave kit unlocked before running.' -ForegroundColor Yellow
        Write-Host 'No button presses needed unless -Pause is set — the script reboots back to dashboard when finished.' -ForegroundColor Yellow
    }
}
else {
    $env:RXDK_KIT_ALLOW_LAUNCH = '1'
}

Write-Host ''
Write-Host 'Running Comprehensive_kit_report...' -ForegroundColor Cyan
Write-Host ''

$testOutDir = Join-Path $repoRoot "src-dotnet\Rxdk.Xbdm.Tests\bin\$Configuration\net8.0"
$kitTestExe = Join-Path $testOutDir 'Rxdk.Xbdm.Tests.exe'
$kitTestDll = Join-Path $testOutDir 'Rxdk.Xbdm.Tests.dll'

if (Test-Path -LiteralPath $kitTestExe) {
    & $kitTestExe --kit
}
elseif (Test-Path -LiteralPath $kitTestDll) {
    dotnet exec $kitTestDll --kit
}
else {
    throw "Kit test runner not found at $kitTestExe"
}

$exitCode = $LASTEXITCODE

Write-Host ''
if (Test-Path -LiteralPath $ReportPath) {
    Write-Host "Report written: $ReportPath" -ForegroundColor Green
    Get-Content -LiteralPath $ReportPath -TotalCount 8 | ForEach-Object { Write-Host $_ }

    if ($OpenReport) {
        Start-Process -FilePath $ReportPath
    }
}
else {
    Write-Host 'Report file was not created (kit unreachable or test skipped early).' -ForegroundColor Yellow
}

if ($exitCode -ne 0) {
    Write-Host ''
    Write-Host 'Hardware test FAILED — see output above and the report for details.' -ForegroundColor Red
    exit $exitCode
}

Write-Host ''
Write-Host 'Hardware test PASSED.' -ForegroundColor Green
exit 0
