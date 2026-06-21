# Publish single-file self-contained managed CLI tools to a flat tools directory.
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
    @{ Project = "Rxdk.XbeCopy\Rxdk.XbeCopy.csproj"; Name = "xbecopy" },
    @{ Project = "Rxdk.ImageBld\Rxdk.ImageBld.csproj"; Name = "imagebld" },
    @{ Project = "Rxdk.XboxLaunch.Cli\Rxdk.XboxLaunch.Cli.csproj"; Name = "xbox-launch" },
    @{ Project = "Rxdk.XboxDbgBridge.Cli\Rxdk.XboxDbgBridge.Cli.csproj"; Name = "xboxdbg-bridge" },
    @{ Project = "Rxdk.XbWatson\Rxdk.XbWatson.csproj"; Name = "xbwatson" }
)

New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "rxdk-cli-publish-$Runtime-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    foreach ($tool in $cliTools) {
        $project = Join-Path $dotnetRoot $tool.Project
        $staging = Join-Path $tempRoot $tool.Name
        New-Item -ItemType Directory -Force -Path $staging | Out-Null

        Write-Host "Publishing $($tool.Name) (single-file, $Runtime)..."
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

Write-Host "Published $($cliTools.Count) single-file tools to: $toolsDir"
