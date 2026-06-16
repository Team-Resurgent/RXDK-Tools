# Compare rsa32_rsa_math.obj vs source xrsa_math.c on isolated math vectors.
param(
    [string]$RepoRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)
$ErrorActionPreference = 'Stop'

$refExe = Join-Path $RepoRoot 'out/bin/Win32/Release/math_compare_ref.exe'
$xrsaExe = Join-Path $RepoRoot 'out/bin/Win32/Release/math_compare_xrsa.exe'
$refOut = Join-Path $RepoRoot 'out/math_compare_ref.txt'
$xrsaOut = Join-Path $RepoRoot 'out/math_compare_xrsa.txt'

$msb = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msb) { throw 'MSBuild not found' }

Write-Host 'Building math_compare_ref and math_compare_xrsa...' -ForegroundColor Cyan
& $msb (Join-Path $RepoRoot 'tests/rsa_math_compare/math_compare_ref.vcxproj') /p:Configuration=Release /p:Platform=Win32 /t:Rebuild /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'math_compare_ref build failed' }
& $msb (Join-Path $RepoRoot 'tests/rsa_math_compare/math_compare_xrsa.vcxproj') /p:Configuration=Release /p:Platform=Win32 /t:Rebuild /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'math_compare_xrsa build failed' }

function Invoke-MathExe {
    param([string]$ExePath, [string]$OutPath)
    $out = & $ExePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Exit $($LASTEXITCODE): $ExePath`n$out"
    }
    $out | Set-Content -Encoding ascii $OutPath
}

Invoke-MathExe -ExePath $refExe -OutPath $refOut
Invoke-MathExe -ExePath $xrsaExe -OutPath $xrsaOut

$refLines = Get-Content $refOut
$xrsaLines = Get-Content $xrsaOut
$failed = $false

foreach ($name in @('multiply_small', 'square_small', 'mod_small', 'mod768_mul', 'mod768', 'mod768_sq', 'mod768_sqmod', 'math_test')) {
    $refLine = $refLines | Where-Object { $_ -like "${name}:*" } | Select-Object -First 1
    $xrsaLine = $xrsaLines | Where-Object { $_ -like "${name}:*" } | Select-Object -First 1
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

if ($failed) {
    throw 'math_compare mismatch'
}

Write-Host 'All math vectors match rsa32_rsa_math.obj' -ForegroundColor Green
