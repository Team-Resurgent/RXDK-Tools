# Align Xbox Neighborhood console registry with the kit's real XBDM name.

param(
    [string]$KitAddress = '192.168.1.184',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testsProject = Join-Path $repoRoot 'src-dotnet\Rxdk.Xbdm.Tests\Rxdk.Xbdm.Tests.csproj'
$testsDll = Join-Path $repoRoot 'src-dotnet\Rxdk.Xbdm.Tests\bin\Release\net8.0\Rxdk.Xbdm.Tests.dll'

Write-Host "Probing kit at $KitAddress..."
dotnet build $testsProject -c Release -v q
if ($LASTEXITCODE -ne 0) {
    dotnet build $testsProject -c Release
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed for Rxdk.Xbdm.Tests' }
}

$probeLines = @(dotnet $testsDll --debugname $KitAddress 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw ($probeLines -join [Environment]::NewLine)
}

$wireName = $null
foreach ($line in $probeLines) {
    if ($line -match '^DEBUGNAME:\s*(.+)\s*$') {
        $wireName = $Matches[1].Trim()
        break
    }
}

if ([string]::IsNullOrWhiteSpace($wireName)) {
    throw "Could not read DEBUGNAME from kit at $KitAddress. Is the kit powered on and reachable?"
}

Write-Host "Kit XBDM name: $wireName"

$consoleKeyPath = 'HKCU:\Software\Microsoft\XboxSDK\xbshlext\Consoles'
$addressKeyPath = 'HKCU:\Software\Microsoft\XboxSDK\xbshlext\Addresses'
$xboxSdkPath = 'HKCU:\Software\Microsoft\XboxSDK'

if (-not $WhatIf) {
    if (-not (Test-Path -LiteralPath $consoleKeyPath)) {
        New-Item -Path $consoleKeyPath -Force | Out-Null
    }

    if (-not (Test-Path -LiteralPath $addressKeyPath)) {
        New-Item -Path $addressKeyPath -Force | Out-Null
    }
}

$existingNames = @()
if (Test-Path -LiteralPath $consoleKeyPath) {
    $existingNames = @(Get-ItemProperty -LiteralPath $consoleKeyPath |
        ForEach-Object { $_.PSObject.Properties } |
        Where-Object { $_.Name -notmatch '^PS' -and -not [string]::IsNullOrWhiteSpace($_.Name) } |
        ForEach-Object { $_.Name })
}

$keepNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$null = $keepNames.Add($wireName)

$removed = @()
foreach ($name in $existingNames) {
    if ($keepNames.Contains($name)) {
        continue
    }

    $removed += $name
    if (-not $WhatIf) {
        Remove-ItemProperty -LiteralPath $consoleKeyPath -Name $name -ErrorAction SilentlyContinue
    }
}

if (-not $WhatIf) {
    New-ItemProperty -LiteralPath $consoleKeyPath -Name $wireName -PropertyType DWord -Value 0 -Force | Out-Null
    New-ItemProperty -LiteralPath $consoleKeyPath -Name '(default)' -PropertyType DWord -Value 1 -Force | Out-Null
    New-ItemProperty -LiteralPath $addressKeyPath -Name $wireName -PropertyType String -Value $KitAddress -Force | Out-Null
    New-ItemProperty -LiteralPath $xboxSdkPath -Name 'XboxName' -PropertyType String -Value $wireName -Force | Out-Null
}
else {
    Write-Host '[WhatIf] Would set XboxName=' $wireName
    Write-Host '[WhatIf] Would keep console:' $wireName
    Write-Host '[WhatIf] Would set address:' "$wireName -> $KitAddress"
}

if ($removed.Count -gt 0) {
    Write-Host "Removed stale console name(s): $($removed -join ', ')"
}

Write-Host "Default console is '$wireName'. Cached address: $KitAddress (not listed as a separate console)."
