# Compare rsa32.lib (reference) vs xrsa.lib (source replacement).
param(
    [string]$RepoRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)
$ErrorActionPreference = 'Stop'

$refExe = Join-Path $RepoRoot 'out/bin/Win32/Release/rsa_compare_ref.exe'
$xrsaExe = Join-Path $RepoRoot 'out/bin/Win32/Release/rsa_compare_xrsa.exe'
$refOut = Join-Path $RepoRoot 'out/rsa_compare_ref.txt'
$xrsaOut = Join-Path $RepoRoot 'out/rsa_compare_xrsa.txt'

function Invoke-CompareExe {
    param(
        [string]$ExePath,
        [string]$OutPath,
        [int]$TimeoutSec = 120
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $ExePath
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        $proc.Kill()
        throw "Timed out after ${TimeoutSec}s: $ExePath"
    }
    if ($proc.ExitCode -ne 0) {
        $err = $proc.StandardError.ReadToEnd()
        throw "Exit $($proc.ExitCode): $ExePath`n$err"
    }
    $proc.StandardOutput.ReadToEnd() | Set-Content -Encoding ascii $OutPath
}

$msb = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msb) { throw 'MSBuild not found' }

Write-Host 'Building rsa_compare_ref and rsa_compare_xrsa...' -ForegroundColor Cyan
& $msb (Join-Path $RepoRoot 'tests/rsa_compare/rsa_compare_ref.vcxproj') /p:Configuration=Release /p:Platform=Win32 /t:Rebuild /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'rsa_compare_ref build failed' }
& $msb (Join-Path $RepoRoot 'tests/rsa_compare/rsa_compare_xrsa.vcxproj') /p:Configuration=Release /p:Platform=Win32 /t:Rebuild /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'rsa_compare_xrsa build failed' }

Invoke-CompareExe -ExePath $refExe -OutPath $refOut -TimeoutSec 120
Invoke-CompareExe -ExePath $xrsaExe -OutPath $xrsaOut -TimeoutSec 120

$refLines = Get-Content $refOut
$xrsaLines = Get-Content $xrsaOut
$compareLines = @(
    'sha_message',
    'sha_xccalc_style',
    'benaloh_modexp',
    'bsafe_enc',
    'bsafe_dec',
    'xc_sign_digest'
)
$failed = $false

foreach ($name in $compareLines) {
    $refLine = $refLines | Where-Object { $_ -like "${name}:*" } | Select-Object -First 1
    $xrsaLine = $xrsaLines | Where-Object { $_ -like "${name}:*" } | Select-Object -First 1
    if ($name -eq 'bsafe_roundtrip') {
        $refLine = $refLines | Where-Object { $_ -eq 'bsafe_roundtrip:OK' } | Select-Object -First 1
        $xrsaLine = $xrsaLines | Where-Object { $_ -eq 'bsafe_roundtrip:OK' } | Select-Object -First 1
    }
    if (-not $refLine -or -not $xrsaLine) {
        Write-Host "MISSING $name" -ForegroundColor Red
        $failed = $true
        continue
    }
    if ($refLine -eq $xrsaLine) {
        Write-Host "PASS $name" -ForegroundColor Green
    } else {
        Write-Host "FAIL $name" -ForegroundColor Red
        Write-Host "  ref:  $refLine"
        Write-Host "  xrsa: $xrsaLine"
        $failed = $true
    }
}

$refOk = $refLines | Where-Object { $_ -eq 'bsafe_roundtrip:OK' } | Select-Object -First 1
$xrsaOk = $xrsaLines | Where-Object { $_ -eq 'bsafe_roundtrip:OK' } | Select-Object -First 1
if ($refOk -and $xrsaOk) {
    Write-Host 'PASS bsafe_roundtrip' -ForegroundColor Green
} else {
    Write-Host 'FAIL bsafe_roundtrip' -ForegroundColor Red
    $failed = $true
}

if ($failed) {
    throw 'rsa_compare mismatch'
}

Write-Host 'All xrsa vectors match rsa32.lib' -ForegroundColor Green
