# Stage managed Rxdk.XbShellExt and native shell proxy for local registration.



param(

    [ValidateSet('Release', 'Debug')]

    [string]$Configuration = 'Release',

    [switch]$SkipBuild

)



$ErrorActionPreference = 'Stop'



$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path



Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force



$shellExtProject = Join-Path $repoRoot 'src-dotnet\Rxdk.XbShellExt\Rxdk.XbShellExt.csproj'

$shellProxyProject = Join-Path $repoRoot 'src-dotnet\Rxdk.XbShellExt.Shell\Rxdk.XbShellExt.Shell.vcxproj'



if (-not $SkipBuild) {

    Write-Host "Building Rxdk.XbShellExt ($Configuration|win-x64)..."

    dotnet build $shellExtProject -c $Configuration -r win-x64

    if ($LASTEXITCODE -ne 0) {

        throw 'dotnet build failed for Rxdk.XbShellExt'

    }



    Write-Host "Building Rxdk.XbShellExt.Shell ($Configuration|x64)..."
    $msbuild = Resolve-MSBuildPath
    & $msbuild $shellProxyProject /p:Configuration=$Configuration /p:Platform=x64 /v:m
    if ($LASTEXITCODE -ne 0) {
        throw 'msbuild failed for Rxdk.XbShellExt.Shell'
    }

}



$buildOut = Join-Path $repoRoot "src-dotnet\Rxdk.XbShellExt\bin\$Configuration\net8.0-windows\win-x64"

if (-not (Test-Path -LiteralPath $buildOut)) {

    throw "Missing build output: $buildOut"

}

$comHostBuild = Join-Path $buildOut 'Rxdk.XbShellExt.comhost.dll'
Assert-XbShellExtComHostClsidMap -ComHostPath $comHostBuild



$shellProxyOut = Join-Path $repoRoot "out\bin\x64\$Configuration\Rxdk.XbShellExt.Shell.dll"

if (-not (Test-Path -LiteralPath $shellProxyOut)) {

    throw "Missing native shell proxy build output: $shellProxyOut"

}



$stageSelection = Select-XbShellExtNextStageDir -RepoRoot $repoRoot

$stageDir = $stageSelection.Path



$stagedNames = New-Object 'System.Collections.Generic.List[string]'

foreach ($file in Get-ChildItem -LiteralPath $buildOut -File) {

    Copy-XbShellExtStageFile -Source $file.FullName -Destination (Join-Path $stageDir $file.Name)

    $stagedNames.Add($file.Name)

}



Copy-XbShellExtStageFile -Source $shellProxyOut -Destination (Join-Path $stageDir 'Rxdk.XbShellExt.Shell.dll')

$stagedNames.Add('Rxdk.XbShellExt.Shell.dll')



$iconSource = Join-Path $repoRoot 'assets\shell\console.ico'

if (Test-Path -LiteralPath $iconSource) {

    Copy-XbShellExtStageFile -Source $iconSource -Destination (Join-Path $stageDir 'console.ico')

    $stagedNames.Add('console.ico')

}



Remove-XbShellExtStaleStageFiles -StageDir $stageDir -KeepNames $stagedNames

Set-XbShellExtActiveSlot -StageRoot $stageSelection.Root -Slot $stageSelection.Slot



Write-Host "Staged shell extension to: $stageDir ($($stageSelection.Slot))"

Get-ChildItem -LiteralPath $stageDir -File | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Name)" }


