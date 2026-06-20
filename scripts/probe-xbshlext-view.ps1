# Verify XboxFolder CoCreate and CreateViewObject via ProbeXbCom.
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force

Write-Host "Registered: $(Get-XbShellExtRegisteredPath)"
$probeProject = Join-Path $PSScriptRoot 'ProbeXbCom\ProbeXbCom.csproj'
dotnet run --project $probeProject -c Release --no-build 2>$null
if ($LASTEXITCODE -ne 0) {
    dotnet run --project $probeProject -c Release
}
exit $LASTEXITCODE
