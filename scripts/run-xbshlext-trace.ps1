param(
    [int]$WaitSeconds = 12,
    [switch]$NoOpen,
    [switch]$NoBuild,
    [switch]$RestartExplorer
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $env:ProgramData 'Xbox Neighborhood\Logs'
$logPath = Join-Path $logDir 'xb-shlext.log'
$mgdLogPath = Join-Path $logDir 'xb-shlext-mgd.log'

Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force

Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $mgdLogPath -Force -ErrorAction SilentlyContinue

if (-not $NoBuild) {
    $msbuildArgs = @(
        (Join-Path $repoRoot 'src\Rxdk.XbShellExt.Shell\Rxdk.XbShellExt.Shell.vcxproj'),
        '/p:Configuration=Release',
        '/p:Platform=x64',
        '/p:XbShellExtTrace=true',
        '/v:m'
    )
    & (Resolve-MSBuildPath) @msbuildArgs | Out-Host
    $built = Join-Path $repoRoot 'out\bin\x64\Release\Rxdk.XbShellExt.Shell.dll'
    $staged = Join-Path $repoRoot 'out\dev\xbshlext\slot-a\Rxdk.XbShellExt.Shell.dll'
    Copy-Item -Force $built $staged
    Write-Host "Staged: $staged"
}

if ($RestartExplorer) {
    Write-Host 'Restarting Explorer so the new Shell.dll is loaded...'
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process explorer.exe
    Start-Sleep -Seconds 3
}

if (-not $NoOpen) {
    if (Test-Administrator) {
        Write-Warning 'Run without admin so ShellExecute can open the namespace.'
    }
    else {
        $shellDll = Join-Path $repoRoot 'out\dev\xbshlext\slot-a\Rxdk.XbShellExt.Shell.dll'
        Start-Process -FilePath "$env:SystemRoot\System32\rundll32.exe" -ArgumentList @($shellDll, 'OpenNamespace')
        Write-Host "Waiting ${WaitSeconds}s for Explorer to bind the namespace..."
        Start-Sleep -Seconds $WaitSeconds
    }
}

Write-Host ""
Write-Host "Native trace log: $logPath"
Write-Host "Managed trace log: $mgdLogPath"
if (Test-Path -LiteralPath $logPath) {
    $lines = Get-Content -LiteralPath $logPath
    $lines | ForEach-Object { Write-Host $_ }

    $hasExplorer = $lines | Where-Object { $_ -match 'process=.*\\explorer\.exe' -or $_ -match 'DllGetClassObject CC44' -or $_ -match '>> Initialize' }
    $launcherOnly = ($lines | Where-Object { $_ -match 'OpenNamespaceViaShell' }).Count -gt 0 -and -not $hasExplorer

    Write-Host ""
    if ($launcherOnly) {
        Write-Host 'WARNING: Log only shows the rundll32 launcher, not Explorer loading the shell extension.'
        Write-Host 'Explorer keeps the old DLL until restarted. Re-run with -RestartExplorer (run-xbshlext-trace.cmd does this by default).'
    }
    elseif ($hasExplorer) {
        Write-Host 'Explorer did load the traced Shell.dll (look for process=...\explorer.exe above).'
    }
}
else {
    Write-Host '(no native log file)'
}

if (Test-Path -LiteralPath $mgdLogPath) {
    Write-Host ''
    Write-Host '--- managed log ---'
    Get-Content -LiteralPath $mgdLogPath | ForEach-Object { Write-Host $_ }
}

Write-Host ""
Write-Host 'How to read the log:'
Write-Host '  >> MethodName  = entered'
Write-Host '  << MethodName  = returned'
Write-Host '  .. MethodName  = note'
Write-Host '  Hang = last >> without a matching <<'
Write-Host '  EnsureInner / CreateManagedFolder in the log = managed comhost load (likely deadlock if stuck there)'
