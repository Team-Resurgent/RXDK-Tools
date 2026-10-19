# Publish single-file framework-dependent managed CLI tools to a flat tools directory.
param(
    [Parameter(Mandatory = $true)]
    [string]$Runtime,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $repoRoot "src"
$toolsDir = if ($OutputDir) { $OutputDir } else { Join-Path $repoRoot "out\publish\managed-cli-tools-$Runtime" }

$cliTools = @(
    @{ Project = "Rxdk.XbSet\Rxdk.XbSet.csproj"; Name = "xbset" },
    @{ Project = "Rxdk.XbCp\Rxdk.XbCp.csproj"; Name = "xbcp" },
    @{ Project = "Rxdk.XbDir\Rxdk.XbDir.csproj"; Name = "xbdir" },
    @{ Project = "Rxdk.XbMkdir\Rxdk.XbMkdir.csproj"; Name = "xbmkdir" },
    @{ Project = "Rxdk.XbDel\Rxdk.XbDel.csproj"; Name = "xbdel" },
    @{ Project = "Rxdk.XbCapture\Rxdk.XbCapture.csproj"; Name = "xbcapture" },
    @{ Project = "Rxdk.XbeCopy\Rxdk.XbeCopy.csproj"; Name = "xbecopy" },
    @{ Project = "Rxdk.ImageBld\Rxdk.ImageBld.csproj"; Name = "imagebld" },
    @{ Project = "Rxdk.Bundler\Rxdk.Bundler.csproj"; Name = "bundler" },
    @{ Project = "Rxdk.XactBld\Rxdk.XactBld.csproj"; Name = "xactbld" },
    @{ Project = "Rxdk.SkinBld\Rxdk.SkinBld.csproj"; Name = "skinbld" },
    @{ Project = "Rxdk.Xsasm\Rxdk.Xsasm.csproj"; Name = "xsasm" },
    @{ Project = "Rxdk.XboxLaunch.Cli\Rxdk.XboxLaunch.Cli.csproj"; Name = "xbox-launch" },
    @{ Project = "Rxdk.XboxDbgBridge.Cli\Rxdk.XboxDbgBridge.Cli.csproj"; Name = "xboxdbg-bridge" },
    # The shared build engine + debug adapter (moved here from RXDK-VS20XX). Both IDEs consume these
    # from the tools bundle: Rxdk.Cli builds/links/packs a title; Rxdk.Dap is the debug adapter.
    @{ Project = "Rxdk.Cli\Rxdk.Cli.csproj"; Name = "Rxdk.Cli" },
    @{ Project = "Rxdk.Dap\Rxdk.Dap.csproj"; Name = "Rxdk.Dap" },
    @{ Project = "Rxdk.XbWatson\Rxdk.XbWatson.csproj"; Name = "xbwatson" },
    # Avalonia GUI app, same single-file publish shape as xbwatson (SingleFilePublish.props
    # embeds the Avalonia native libs), so it ships as one flat exe in tools/ too.
    @{ Project = "Rxdk.XbNeighborhood\Rxdk.XbNeighborhood.csproj"; Name = "xbneighborhood" }
)

New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "rxdk-cli-publish-$Runtime-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    foreach ($tool in $cliTools) {
        $project = Join-Path $dotnetRoot $tool.Project
        $staging = Join-Path $tempRoot $tool.Name
        New-Item -ItemType Directory -Force -Path $staging | Out-Null

        Write-Host "Publishing $($tool.Name) (single-file framework-dependent, $Runtime)..."
        dotnet publish $project -c Release -r $Runtime -o $staging
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $publishedExe = Get-ChildItem -Path $staging -File |
            Where-Object { $_.BaseName -eq $tool.Name } |
            Select-Object -First 1
        if ($null -eq $publishedExe) {
            $names = (Get-ChildItem -Path $staging -File | ForEach-Object { $_.Name }) -join ", "
            throw "Expected published executable '$($tool.Name)' for $($tool.Name), found: $names"
        }

        $source = $publishedExe.FullName
        $extension = $publishedExe.Extension
        $destination = Join-Path $toolsDir ($tool.Name + $extension)
        Copy-Item -LiteralPath $source -Destination $destination -Force
        Write-Host "  -> $destination"
    }
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Published $($cliTools.Count) single-file framework-dependent tools to: $toolsDir"
