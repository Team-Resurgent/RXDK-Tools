# Unregister the xbshlext shell extension.

param(

    [string]$DllPath,

    [switch]$Force,

    [switch]$ElevatedChild

)



$ErrorActionPreference = 'Stop'



Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force

Assert-Administrator -AutoElevate -ScriptPath $PSCommandPath -BoundParameters $PSBoundParameters



try {

    if ([string]::IsNullOrWhiteSpace($DllPath)) {

        $DllPath = Get-XbShellExtRegisteredPath

    }



    if ([string]::IsNullOrWhiteSpace($DllPath)) {

        Write-Host 'No shell extension registration found.'

        return

    }



    if ((Test-ProgramFilesPath $DllPath) -and -not $Force) {

        throw "Registered path is under Program Files: $DllPath. Pass -Force to unregister the installed build."

    }



    $stageDir = Split-Path -Parent $DllPath

    $comHost = Join-Path $stageDir 'Rxdk.XbShellExt.comhost.dll'

    if (Test-Path -LiteralPath $comHost) {

        Write-Host "Unregistering managed coclass: $comHost"

        Invoke-XbShellExtRegsvr32 -DllPath $comHost -Unregister

    }



    Write-Host 'Removing shell extension registry keys...'

    Clear-XbShellExtRegistry -ClearUserData



    Write-Host 'Restarting Explorer...'

    Stop-XbShellExtExplorer

    Start-XbShellExtExplorer



    Write-Host 'Shell extension unregistered.'

}

catch {

    Write-XbShellExtScriptError -ErrorRecord $_ -ElevatedChild:$ElevatedChild

    exit 1

}


