Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-AdministratorElevated {
    param(
        [Parameter(Mandatory)]
        [string]$ScriptPath,
        [hashtable]$BoundParameters = @{ }
    )

    $exe = (Get-Process -Id $PID).Path
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath)

    $elevatedParams = @{}
    foreach ($entry in $BoundParameters.GetEnumerator()) {
        $elevatedParams[$entry.Key] = $entry.Value
    }
    $elevatedParams['ElevatedChild'] = $true

    foreach ($entry in $elevatedParams.GetEnumerator()) {
        $name = $entry.Key
        $value = $entry.Value
        if ($value -is [switch]) {
            if ($value.IsPresent) {
                $args += "-$name"
            }
        }
        elseif ($value -is [bool] -and $value) {
            $args += "-$name"
        }
        elseif ($null -ne $value -and "$value" -ne '') {
            $args += "-$name"
            $args += [string]$value
        }
    }

    Write-Host 'Requesting administrator privileges...'
    $proc = Start-Process -FilePath $exe -Verb RunAs -ArgumentList $args -Wait -PassThru
    exit $(if ($null -ne $proc.ExitCode) { $proc.ExitCode } else { 0 })
}

function Assert-Administrator {
    param(
        [switch]$AutoElevate,
        [string]$ScriptPath,
        [hashtable]$BoundParameters
    )

    if (Test-Administrator) {
        return
    }

    if ($AutoElevate -and -not [string]::IsNullOrWhiteSpace($ScriptPath)) {
        Invoke-AdministratorElevated -ScriptPath $ScriptPath -BoundParameters $BoundParameters
    }

    throw 'Administrator privileges are required. Re-run from an elevated PowerShell prompt.'
}

function Get-XbShellExtClsidPath {
    param([Microsoft.Win32.RegistryView]$View = [Microsoft.Win32.RegistryView]::Registry64)

    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::ClassesRoot, $View)
    $subKey = $baseKey.OpenSubKey('CLSID\{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}\InprocServer32')
    if ($null -eq $subKey) {
        return $null
    }

    try {
        return [string]$subKey.GetValue('')
    }
    finally {
        $subKey.Dispose()
        $baseKey.Dispose()
    }
}

function Get-XbShellExtRegisteredPath {
    Get-XbShellExtClsidPath
}

function Get-XbShellExtStageRoot {
    param([string]$RepoRoot)

    if (-not [string]::IsNullOrWhiteSpace($env:RXDK_SHELL_EXT_DIR)) {
        return $null
    }

    return (Join-Path $RepoRoot 'out\dev\xbshlext')
}

function Get-XbShellExtSlotDir {
    param(
        [Parameter(Mandatory)][string]$StageRoot,
        [Parameter(Mandatory)][ValidateSet('slot-a', 'slot-b')][string]$Slot
    )

    Join-Path $StageRoot $Slot
}

function Get-XbShellExtActiveSlotName {
    param([Parameter(Mandatory)][string]$StageRoot)

    $marker = Join-Path $StageRoot 'active.slot'
    if (Test-Path -LiteralPath $marker) {
        $name = (Get-Content -LiteralPath $marker -Raw).Trim()
        if ($name -eq 'slot-a' -or $name -eq 'slot-b') {
            return $name
        }
    }

    $registered = Get-XbShellExtRegisteredPath
    if ($registered -like '*\slot-a\*') { return 'slot-a' }
    if ($registered -like '*\slot-b\*') { return 'slot-b' }
    return 'slot-a'
}

function Set-XbShellExtActiveSlot {
    param(
        [Parameter(Mandatory)][string]$StageRoot,
        [Parameter(Mandatory)][ValidateSet('slot-a', 'slot-b')][string]$Slot
    )

    Set-Content -LiteralPath (Join-Path $StageRoot 'active.slot') -Value $Slot -Encoding ascii -NoNewline
}

function Sync-XbShellExtActiveSlotFromRegistry {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $stageRoot = Get-XbShellExtStageRoot -RepoRoot $RepoRoot
    if (-not $stageRoot) {
        return
    }

    $registered = Get-XbShellExtRegisteredPath
    if ($registered -like '*\slot-a\*') {
        Set-XbShellExtActiveSlot -StageRoot $stageRoot -Slot 'slot-a'
    }
    elseif ($registered -like '*\slot-b\*') {
        Set-XbShellExtActiveSlot -StageRoot $stageRoot -Slot 'slot-b'
    }
}

function Get-XbShellExtInactiveSlotName {
    param([Parameter(Mandatory)][string]$StageRoot)

    $registered = Get-XbShellExtRegisteredPath
    if ($registered -like '*\slot-a\*') { return 'slot-b' }
    if ($registered -like '*\slot-b\*') { return 'slot-a' }

    $active = Get-XbShellExtActiveSlotName -StageRoot $StageRoot
    if ($active -eq 'slot-a') { return 'slot-b' }
    return 'slot-a'
}

function Test-XbShellExtSlotStageable {
    param([Parameter(Mandatory)][string]$SlotDir)

    New-Item -ItemType Directory -Force -Path $SlotDir | Out-Null

    foreach ($fileName in @('Rxdk.XbShellExt.comhost.dll', 'Rxdk.XbShellExt.Shell.dll')) {
        $path = Join-Path $SlotDir $fileName
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        try {
            $stream = [System.IO.File]::Open(
                $path,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
            $stream.Close()
        }
        catch {
            return $false
        }
    }

    return $true
}

function Resolve-XbShellExtStageSlot {
    param([Parameter(Mandatory)][string]$StageRoot)

    $registered = Get-XbShellExtRegisteredPath
    $candidates = [System.Collections.Generic.List[string]]::new()

    if ($registered -like '*\slot-a\*') {
        [void]$candidates.Add('slot-b')
        [void]$candidates.Add('slot-a')
    }
    elseif ($registered -like '*\slot-b\*') {
        [void]$candidates.Add('slot-a')
        [void]$candidates.Add('slot-b')
    }
    else {
        $preferred = Get-XbShellExtInactiveSlotName -StageRoot $StageRoot
        $alternate = if ($preferred -eq 'slot-a') { 'slot-b' } else { 'slot-a' }
        [void]$candidates.Add($preferred)
        [void]$candidates.Add($alternate)
    }

    $seen = @{}
    foreach ($slot in $candidates) {
        if ($seen.ContainsKey($slot)) {
            continue
        }
        $seen[$slot] = $true

        if ($registered -and ($registered -like "*\$slot\*")) {
            continue
        }

        $dir = Get-XbShellExtSlotDir -StageRoot $StageRoot -Slot $slot
        if (Test-XbShellExtSlotStageable -SlotDir $dir) {
            return $slot
        }
    }

    return $null
}

function Select-XbShellExtNextStageDir {
    param([Parameter(Mandatory)][string]$RepoRoot)

    if (-not [string]::IsNullOrWhiteSpace($env:RXDK_SHELL_EXT_DIR)) {
        $flatDir = (Resolve-Path -LiteralPath $env:RXDK_SHELL_EXT_DIR).Path
        New-Item -ItemType Directory -Force -Path $flatDir | Out-Null
        Clear-XbShellExtStageDir -StageDir $flatDir
        return @{
            Root = $flatDir
            Slot = 'flat'
            Path = $flatDir
        }
    }

    $stageRoot = Get-XbShellExtStageRoot -RepoRoot $RepoRoot
    New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

    $next = Resolve-XbShellExtStageSlot -StageRoot $stageRoot
    if (-not $next) {
        Write-Host 'Stage slots are locked. Stopping Explorer and rechecking...'
        Stop-XbShellExtExplorer
        Start-Sleep -Seconds 2
        $next = Resolve-XbShellExtStageSlot -StageRoot $stageRoot
    }

    if (-not $next) {
        throw @(
            'Both dev stage slots are locked (Rxdk.XbShellExt.comhost.dll is still in use).'
            'Close all File Explorer windows, run .\scripts\unregister-xbshlext-dev.ps1 -Force, reboot if needed, then retry register.'
        ) -join ' '
    }

    $nextDir = Get-XbShellExtSlotDir -StageRoot $stageRoot -Slot $next
    New-Item -ItemType Directory -Force -Path $nextDir | Out-Null
    Clear-XbShellExtStageDir -StageDir $nextDir

    return @{
        Root = $stageRoot
        Slot = $next
        Path = $nextDir
    }
}

function Get-XbShellExtDefaultStageDir {
    param([string]$RepoRoot)

    if (-not [string]::IsNullOrWhiteSpace($env:RXDK_SHELL_EXT_DIR)) {
        return (Resolve-Path -LiteralPath $env:RXDK_SHELL_EXT_DIR).Path
    }

    $stageRoot = Join-Path $RepoRoot 'out\dev\xbshlext'
    $active = Get-XbShellExtActiveSlotName -StageRoot $stageRoot
    return Get-XbShellExtSlotDir -StageRoot $stageRoot -Slot $active
}

function Get-XbShellExtShellDllPath {
    param([string]$RepoRoot)

    $stageDir = Get-XbShellExtDefaultStageDir -RepoRoot $RepoRoot
    $shellDll = Join-Path $stageDir 'Rxdk.XbShellExt.Shell.dll'
    if (Test-Path -LiteralPath $shellDll) {
        return $shellDll
    }

    return $null
}

function Get-XbShellExtComHostPath {
    param([string]$RepoRoot)

    $stageDir = Get-XbShellExtDefaultStageDir -RepoRoot $RepoRoot
    $comHost = Join-Path $stageDir 'Rxdk.XbShellExt.comhost.dll'
    if (Test-Path -LiteralPath $comHost) {
        return $comHost
    }

    return Join-Path $stageDir 'Rxdk.XbShellExt.dll'
}

function Resolve-MSBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw 'vswhere.exe not found. Install Visual Studio with the Desktop development with C++ workload.'
    }

    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path -LiteralPath $msbuild)) {
        throw 'MSBuild.exe not found. Install Visual Studio with the Desktop development with C++ workload.'
    }

    return $msbuild
}

function Stop-XbShellExtExplorer {
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        $null = Start-Process -FilePath 'taskkill.exe' -ArgumentList '/F', '/IM', 'explorer.exe' -Wait -WindowStyle Hidden -ErrorAction SilentlyContinue
        Start-Sleep -Seconds (2 + $attempt)
        if (-not (Get-Process -Name 'explorer' -ErrorAction SilentlyContinue)) {
            Start-Sleep -Seconds 1
            return
        }
    }

    throw 'Explorer is still running and may be locking Rxdk.XbShellExt.comhost.dll. Close any File Explorer windows and retry.'
}

function Start-XbShellExtExplorer {
    Start-Sleep -Seconds 1
    $null = Start-Process -FilePath 'explorer.exe'
}

function Invoke-XbShellExtRegsvr32 {
    param(
        [Parameter(Mandatory)]
        [string]$DllPath,
        [switch]$Unregister
    )

    if (-not (Test-Path -LiteralPath $DllPath)) {
        throw "DLL not found: $DllPath"
    }

    $args = @('/s')
    if ($Unregister) {
        $args += '/u'
    }
    $args += "`"$DllPath`""

    $proc = Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" -ArgumentList $args -Wait -PassThru -WindowStyle Hidden
    if ($proc.ExitCode -ne 0) {
        $action = if ($Unregister) { 'DllUnregisterServer' } else { 'DllRegisterServer' }
        $codeHex = ('0x{0:x8}' -f ([uint32]$proc.ExitCode))
        $hint = switch ($proc.ExitCode) {
            1 { 'LoadLibrary failed - a dependency DLL is missing next to xbshlext.dll.' }
            2 { 'GetProcAddress failed - the DLL export is missing or mismatched.' }
            3 { "$action failed - registry update was rejected (run elevated, or pass -Force if replacing an installed build)." }
            5 { 'Access denied. Re-run from an elevated PowerShell prompt.' }
            -1073740791 { 'The shell extension crashed during load or registration (0xC0000409). Check Rxdk.XbShellExt.comhost.dll, Rxdk.XbShellExt.runtimeconfig.json, and dependency DLLs are staged beside the comhost.' }
            default { "See regsvr32 documentation for exit code $codeHex." }
        }
        throw "regsvr32 $(if ($Unregister) { '/u ' })failed with exit code $codeHex ($($proc.ExitCode)) for $DllPath. $hint"
    }
}

function Repair-XbShellExtRegistry {
    param(
        [string]$ModulePath
    )

    $clsid = '{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}'
    $clsidBare = 'DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44'
    $clsidKey = "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid"
    if (-not (Test-Path -LiteralPath $clsidKey)) {
        New-Item -Path $clsidKey -Force | Out-Null
    }

    Set-ItemProperty -LiteralPath $clsidKey -Name '(default)' -Value 'Xbox Neighborhood'
    Set-ItemProperty -LiteralPath $clsidKey -Name 'ProgID' -Value 'Shellext.XboxFolder.1'
    Set-ItemProperty -LiteralPath $clsidKey -Name 'VersionIndependentProgID' -Value 'Shellext.XboxFolder'
    # Pin to the Explorer navigation pane (required on Win10/11 alongside NameSpace registration).
    Set-ItemProperty -LiteralPath $clsidKey -Name 'System.IsPinnedToNameSpaceTree' -Value 1 -Type DWord
    # Place near other This PC items (Network uses 0x58).
    Set-ItemProperty -LiteralPath $clsidKey -Name 'SortOrderIndex' -Value 0x50 -Type DWord

    $inprocKey = "$clsidKey\InprocServer32"
    if (-not (Test-Path -LiteralPath $inprocKey)) {
        New-Item -Path $inprocKey -Force | Out-Null
    }
    if ($ModulePath) {
        Set-ItemProperty -LiteralPath $inprocKey -Name '(default)' -Value $ModulePath
    }
    Set-ItemProperty -LiteralPath $inprocKey -Name 'ThreadingModel' -Value 'Apartment'

    $shellFolderKey = "$clsidKey\ShellFolder"
    if (-not (Test-Path -LiteralPath $shellFolderKey)) {
        New-Item -Path $shellFolderKey -Force | Out-Null
    }
    # SFGAO_HASSUBFOLDER (0x80000000) | SFGAO_FOLDER (0x20000000) | SFGAO_CANLINK (0x4).
    # The original XboxFolder.rgs stored this as REG_BINARY bytes 04 00 00 A0,
    # i.e. the little-endian DWORD 0xA0000004. Writing 0x040000A0 instead drops
    # the SFGAO_FOLDER bit, so the shell treats CC44 as a non-folder and refuses
    # to open it ("no app associated with it for performing this action").
    Set-ItemProperty -LiteralPath $shellFolderKey -Name 'Attributes' -Value 0xA0000004 -Type DWord

    if ($ModulePath) {
        $iconPath = Join-Path (Split-Path -Parent $ModulePath) 'console.ico'
        $defaultIconKey = "$clsidKey\DefaultIcon"
        if (-not (Test-Path -LiteralPath $defaultIconKey)) {
            New-Item -Path $defaultIconKey -Force | Out-Null
        }
        $iconValue = if (Test-Path -LiteralPath $iconPath) { $iconPath } else { "$ModulePath,0" }
        Set-ItemProperty -LiteralPath $defaultIconKey -Name '(default)' -Value $iconValue
    }

    $progIdKey = 'Registry::HKEY_CLASSES_ROOT\Shellext.XboxFolder.1'
    if (-not (Test-Path -LiteralPath $progIdKey)) {
        New-Item -Path $progIdKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $progIdKey -Name '(default)' -Value 'Xbox Neighborhood'
    Set-ItemProperty -LiteralPath $progIdKey -Name 'CLSID' -Value $clsid

    $viProgIdKey = 'Registry::HKEY_CLASSES_ROOT\Shellext.XboxFolder'
    if (-not (Test-Path -LiteralPath $viProgIdKey)) {
        New-Item -Path $viProgIdKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $viProgIdKey -Name '(default)' -Value 'Xbox Neighborhood'
    Set-ItemProperty -LiteralPath $viProgIdKey -Name 'CLSID' -Value $clsid
    Set-ItemProperty -LiteralPath $viProgIdKey -Name 'CurVer' -Value 'Shellext.XboxFolder.1'

    # A shell namespace folder must NOT register a shell\open\command. If one
    # exists, Explorer invokes that verb (relaunching explorer.exe) instead of
    # binding to our IShellFolder + CreateViewObject, so the view never loads
    # (brief flash, then an endless busy spinner). Matches the original native
    # XboxFolder.rgs, which has no open verb on the CLSID.
    foreach ($shellKey in @(
            "$progIdKey\shell"
            "$clsidKey\shell"
        )) {
        if (Test-Path -LiteralPath $shellKey) {
            Remove-Item -LiteralPath $shellKey -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    $desktopKey = "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\$clsid"
    if (-not (Test-Path -LiteralPath $desktopKey)) {
        New-Item -Path $desktopKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $desktopKey -Name '(default)' -Value 'Xbox Neighborhood'

    $myComputerKey = "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\$clsid"
    if (-not (Test-Path -LiteralPath $myComputerKey)) {
        New-Item -Path $myComputerKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $myComputerKey -Name '(default)' -Value 'Xbox Neighborhood'

    $approvedKey = 'Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved'
    if (-not (Test-Path -LiteralPath $approvedKey)) {
        New-Item -Path $approvedKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $approvedKey -Name $clsidBare -Value 'Xbox Namespace Shell Extension'

    if ($ModulePath) {
        $rundll32 = Join-Path $env:SystemRoot 'System32\rundll32.exe'
        $xboxKey = 'Registry::HKEY_CLASSES_ROOT\xbox'
        if (-not (Test-Path -LiteralPath $xboxKey)) {
            New-Item -Path $xboxKey -Force | Out-Null
        }
        Set-ItemProperty -LiteralPath $xboxKey -Name '(default)' -Value 'URL:Xbox Namespace Extension'
        New-ItemProperty -LiteralPath $xboxKey -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null

        $openKey = "$xboxKey\shell\open\command"
        if (-not (Test-Path -LiteralPath $openKey)) {
            New-Item -Path $openKey -Force | Out-Null
        }
        $command = "`"$rundll32`" `"$ModulePath`",LaunchExplorer %1"
        Set-ItemProperty -LiteralPath $openKey -Name '(default)' -Value $command
    }

    Repair-XbShellExtNavPaneUserState -Clsid $clsid -ModulePath $ModulePath
}

function Repair-XbShellExtNavPaneUserState {
    param(
        [Parameter(Mandatory)]
        [string]$Clsid,
        [string]$ModulePath
    )

    # Explorer caches per-user ShellFolder attribute overrides under
    # HKCU\...\Explorer\CLSID\{guid}. A stale SFGAO_NONENUMERATED (0x100000)
    # value hides the namespace from the navigation pane even when machine
    # registration is correct.
    $explorerClsidKey = "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\$Clsid"
    $explorerShellFolderKey = "$explorerClsidKey\ShellFolder"
    if (Test-Path -LiteralPath $explorerShellFolderKey) {
        $attrs = (Get-ItemProperty -LiteralPath $explorerShellFolderKey -ErrorAction SilentlyContinue).Attributes
        if ($null -eq $attrs -or $attrs -band 0x100000) {
            Remove-Item -LiteralPath $explorerShellFolderKey -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    $userClassesKey = "Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\$Clsid"
    if (-not (Test-Path -LiteralPath $userClassesKey)) {
        New-Item -Path $userClassesKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $userClassesKey -Name '(default)' -Value 'Xbox Neighborhood'
    Set-ItemProperty -LiteralPath $userClassesKey -Name 'System.IsPinnedToNameSpaceTree' -Value 1 -Type DWord
    Set-ItemProperty -LiteralPath $userClassesKey -Name 'SortOrderIndex' -Value 0x50 -Type DWord

    if ($ModulePath) {
        $userInprocKey = "$userClassesKey\InprocServer32"
        if (-not (Test-Path -LiteralPath $userInprocKey)) {
            New-Item -Path $userInprocKey -Force | Out-Null
        }
        Set-ItemProperty -LiteralPath $userInprocKey -Name '(default)' -Value $ModulePath
        Set-ItemProperty -LiteralPath $userInprocKey -Name 'ThreadingModel' -Value 'Apartment'
    }

    $userDesktopKey = "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\$Clsid"
    if (-not (Test-Path -LiteralPath $userDesktopKey)) {
        New-Item -Path $userDesktopKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $userDesktopKey -Name '(default)' -Value 'Xbox Neighborhood'

    $hideDesktopKey = 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
    if (-not (Test-Path -LiteralPath $hideDesktopKey)) {
        New-Item -Path $hideDesktopKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $hideDesktopKey -Name $Clsid -Value 1 -Type DWord
}

function Repair-XbShellExtManagedRegistry {
    param(
        [Parameter(Mandatory)]
        [string]$ModulePath
    )

    $clsid = '{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45}'
    $clsidKey = "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid"
    if (-not (Test-Path -LiteralPath $clsidKey)) {
        New-Item -Path $clsidKey -Force | Out-Null
    }

    Set-ItemProperty -LiteralPath $clsidKey -Name '(default)' -Value 'Xbox Neighborhood (Managed)'

    $inprocKey = "$clsidKey\InprocServer32"
    if (-not (Test-Path -LiteralPath $inprocKey)) {
        New-Item -Path $inprocKey -Force | Out-Null
    }
    Set-ItemProperty -LiteralPath $inprocKey -Name '(default)' -Value $ModulePath
    Set-ItemProperty -LiteralPath $inprocKey -Name 'ThreadingModel' -Value 'Apartment'
}

function Clear-XbShellExtRegistration {
    $publicClsid = 'DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44'
    $managedClsid = 'DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45'

    Remove-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\CLSID\{$publicClsid}" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\CLSID\{$managedClsid}" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\Shellext.XboxFolder.1" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\Shellext.XboxFolder" -Recurse -Force -ErrorAction SilentlyContinue

    Remove-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\xbox' -Recurse -Force -ErrorAction SilentlyContinue

    Remove-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved' -Name $publicClsid -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{$publicClsid}" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{$publicClsid}" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{$publicClsid}" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{$publicClsid}" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\{$publicClsid}" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -LiteralPath 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel' -Name "{$publicClsid}" -ErrorAction SilentlyContinue
}

function Clear-XbShellExtRegistry {
    param(
        [switch]$ClearUserData
    )

    Clear-XbShellExtRegistration

    if ($ClearUserData) {
        Remove-Item -LiteralPath 'Registry::HKEY_CURRENT_USER\Software\Microsoft\XboxSDK\xbshlext' -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Enable-ExplorerNavPaneShowAllFolders {
    $advancedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    if (-not (Test-Path -LiteralPath $advancedKey)) {
        New-Item -Path $advancedKey -Force | Out-Null
    }

    Set-ItemProperty -LiteralPath $advancedKey -Name 'NavPaneShowAllFolders' -Type DWord -Value 1
}

function Open-XbShellExtNamespace {
    param(
        [string]$ShellDllPath
    )

    if (Test-Administrator) {
        $openCmd = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'open-xbox-neighborhood.cmd'
        Write-Warning "Skipping namespace launch from elevated session (ShellExecute error 5). Run without admin: $openCmd"
        return
    }

    if (-not $ShellDllPath) {
        $ShellDllPath = Get-XbShellExtShellDllPath
    }

    Enable-ExplorerNavPaneShowAllFolders

    Start-Process -FilePath "$env:SystemRoot\System32\rundll32.exe" -ArgumentList @(
        $ShellDllPath,
        'OpenNamespace'
    ) -ErrorAction Stop
}

function Test-ProgramFilesPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $normalized = $Path.ToLowerInvariant()
    return $normalized.Contains('\program files\') -or $normalized.Contains('\program files (x86)\')
}

function Write-XbShellExtScriptError {
    param(
        $ErrorRecord,
        [switch]$ElevatedChild
    )

    Write-Host ''
    Write-Host 'Shell extension script failed:' -ForegroundColor Red
    Write-Host $ErrorRecord.Exception.Message -ForegroundColor Red
    if ($ErrorRecord.ScriptStackTrace) {
        Write-Host $ErrorRecord.ScriptStackTrace -ForegroundColor DarkRed
    }
    elseif ($ErrorRecord.InvocationInfo) {
        Write-Host (
            "At $($ErrorRecord.InvocationInfo.ScriptName):$($ErrorRecord.InvocationInfo.ScriptLineNumber)"
        ) -ForegroundColor DarkRed
    }

    if ($ElevatedChild) {
        Read-Host 'Press Enter to close'
    }
}

function Clear-XbShellExtStageDir {
    param([Parameter(Mandatory)][string]$StageDir)

    if (-not (Test-Path -LiteralPath $StageDir)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $StageDir -File -ErrorAction SilentlyContinue) {
        try {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
        }
        catch {
            Write-Host "  Skipped locked staged file: $($file.Name)"
        }
    }
}

function Copy-XbShellExtStageFile {
    param(
        [Parameter(Mandatory)]
        [string]$Source,
        [Parameter(Mandatory)]
        [string]$Destination
    )

    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        try {
            Copy-Item -LiteralPath $Source -Destination $Destination -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -ge 3) {
                throw
            }

            Write-Host "Shell extension files are locked ($($_.Exception.Message)). Stopping Explorer and retrying copy..."
            Stop-XbShellExtExplorer
            Start-Sleep -Seconds (2 + $attempt)
        }
    }
}

function Assert-XbShellExtComHostClsidMap {
    param(
        [Parameter(Mandatory)]
        [string]$ComHostPath
    )

    if (-not (Test-Path -LiteralPath $ComHostPath)) {
        throw "Missing comhost: $ComHostPath"
    }

    $managedClsid = 'db15fedd-96b8-4da9-97e0-7e5cca05cc45'
    $publicClsid = 'db15fedd-96b8-4da9-97e0-7e5cca05cc44'
    $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($ComHostPath))

    if ($text -notmatch $managedClsid) {
        throw "Rxdk.XbShellExt.comhost.dll is missing managed coclass CC45 in its embedded clsidmap: $ComHostPath"
    }

    if ($text -match "\{$publicClsid\}") {
        throw @(
            "Rxdk.XbShellExt.comhost.dll still embeds CC44 in its clsidmap (stale build output)."
            "Run: dotnet clean src-dotnet/Rxdk.XbShellExt/Rxdk.XbShellExt.csproj -c Release"
            "Then rebuild and stage again."
        ) -join ' '
    }
}

function Remove-XbShellExtStaleStageFiles {
    param(
        [Parameter(Mandatory)][string]$StageDir,
        [Parameter(Mandatory)][string[]]$KeepNames
    )

    $keep = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $KeepNames) { $null = $keep.Add($name) }

    foreach ($file in Get-ChildItem -LiteralPath $StageDir -File -ErrorAction SilentlyContinue) {
        if ($keep.Contains($file.Name)) {
            continue
        }

        try {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
        }
        catch {
            Write-Host "  Skipped stale file (locked): $($file.Name)"
        }
    }
}

Export-ModuleMember -Function @(
    'Test-Administrator',
    'Assert-Administrator',
    'Invoke-AdministratorElevated',
    'Get-XbShellExtRegisteredPath',
    'Get-XbShellExtDefaultStageDir',
    'Get-XbShellExtComHostPath',
    'Get-XbShellExtShellDllPath',
    'Resolve-MSBuildPath',
    'Select-XbShellExtNextStageDir',
    'Sync-XbShellExtActiveSlotFromRegistry',
    'Get-XbShellExtInactiveSlotName',
    'Test-XbShellExtSlotStageable',
    'Resolve-XbShellExtStageSlot',
    'Set-XbShellExtActiveSlot',
    'Stop-XbShellExtExplorer',
    'Start-XbShellExtExplorer',
    'Invoke-XbShellExtRegsvr32',
    'Clear-XbShellExtStageDir',
    'Copy-XbShellExtStageFile',
    'Remove-XbShellExtStaleStageFiles',
    'Assert-XbShellExtComHostClsidMap',
    'Repair-XbShellExtRegistry',
    'Repair-XbShellExtNavPaneUserState',
    'Repair-XbShellExtManagedRegistry',
    'Enable-ExplorerNavPaneShowAllFolders',
    'Clear-XbShellExtRegistration',
    'Clear-XbShellExtRegistry',
    'Open-XbShellExtNamespace',
    'Test-ProgramFilesPath',
    'Write-XbShellExtScriptError'
)
