# Run native vs managed XBDM parity tests and write artifacts/xbdm-parity-report.md
#
# Examples:
#   .\scripts\run-xbdm-parity.ps1 -Console MyXbox
#   .\scripts\run-xbdm-parity.ps1 -Console 192.168.1.50 -Password secret -Full
#   .\scripts\run-xbdm-parity.ps1 -Console MyXbox -Quick
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
    [string]$ReportPath = $env:RXDK_PARITY_REPORT
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testsProject = Join-Path $repoRoot 'src-dotnet\Rxdk.Xbdm.Tests\Rxdk.Xbdm.Tests.csproj'
$bridgeProject = Join-Path $repoRoot 'src-dotnet\Rxdk.XboxDbgBridge\Rxdk.XboxDbgBridge.csproj'
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
    $ReportPath = Join-Path $artifactsDir 'xbdm-parity-report.md'
}

Write-Host ''
Write-Host '=== XBDM parity test ===' -ForegroundColor Cyan
Write-Host "Console : $Console"
Write-Host "Mode    : $(if ($Full) { 'Full (launch + bridge + reboot + security)' } else { 'Quick (read-only / file ops)' })"
Write-Host "Report  : $ReportPath"
Write-Host ''

Set-Location $repoRoot

$xbdmNativeProject = Join-Path $repoRoot 'src\xbdm-native\xbdm-native.vcxproj'
Write-Host 'Building xbdm.dll (native)...' -ForegroundColor DarkGray
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' `
    | Select-Object -First 1
if (-not $msbuild) {
    throw 'MSBuild not found. Install Visual Studio with the Desktop development with C++ workload.'
}
& $msbuild $xbdmNativeProject /p:Configuration=Release /p:Platform=x64 /v:m
if ($LASTEXITCODE -ne 0) {
    throw 'xbdm-native build failed.'
}

Write-Host 'Building xboxdbg-bridge...' -ForegroundColor DarkGray
dotnet build $bridgeProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Bridge build failed.'
}

Write-Host 'Building parity tests...' -ForegroundColor DarkGray
dotnet build $testsProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Test project build failed.'
}

$env:RXDK_TEST_CONSOLE = $Console
$env:RXDK_PARITY_REPORT = $ReportPath

if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $env:RXDK_TEST_PASSWORD = $Password
}
else {
    Remove-Item Env:RXDK_TEST_PASSWORD -ErrorAction SilentlyContinue
}

Remove-Item Env:RXDK_PARITY_ALLOW_EXEC -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_PARITY_ALLOW_LAUNCH -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_PARITY_ALLOW_BRIDGE -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_PARITY_ALLOW_REBOOT -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_PARITY_ALLOW_SECURITY -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_PARITY_NO_RESTORE -ErrorAction SilentlyContinue
Remove-Item Env:RXDK_PARITY_PAUSE -ErrorAction SilentlyContinue

if ($Pause) {
    $env:RXDK_PARITY_PAUSE = '1'
}

if ($Full) {
    $env:RXDK_PARITY_ALLOW_EXEC = '1'
    $env:RXDK_PARITY_ALLOW_BRIDGE = '1'
    $env:RXDK_PARITY_ALLOW_REBOOT = '1'
    $env:RXDK_PARITY_ALLOW_SECURITY = '1'
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
    $env:RXDK_PARITY_ALLOW_LAUNCH = '1'
}

Write-Host ''
Write-Host 'Running Comprehensive_parity_report...' -ForegroundColor Cyan
Write-Host ''

$testOutDir = Join-Path $repoRoot "src-dotnet\Rxdk.Xbdm.Tests\bin\$Configuration\net8.0"
$parityExe = Join-Path $testOutDir 'Rxdk.Xbdm.Tests.exe'
$parityDll = Join-Path $testOutDir 'Rxdk.Xbdm.Tests.dll'

if (Test-Path -LiteralPath $parityExe) {
    & $parityExe --parity
}
elseif (Test-Path -LiteralPath $parityDll) {
    dotnet exec $parityDll --parity
}
else {
    throw "Parity runner not found at $parityExe"
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
    Write-Host 'Parity test FAILED — see output above and the report for details.' -ForegroundColor Red
    exit $exitCode
}

Write-Host ''
Write-Host 'Parity test PASSED.' -ForegroundColor Green
exit 0
