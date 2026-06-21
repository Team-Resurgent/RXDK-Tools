# Self-contained Xbox Neighborhood shell extension diagnostic for Windows 10/11.
# Copy this file (and diagnose-xbshlext.cmd) to any PC - no repo or other scripts required.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File diagnose-xbshlext.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File diagnose-xbshlext.ps1 -SaveReport
#   powershell -NoProfile -ExecutionPolicy Bypass -File diagnose-xbshlext.ps1 -TryOpen
param(
    [switch]$SaveReport,
    [switch]$TryOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$Script:ClsidPublic = '{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}'
$Script:ClsidManaged = '{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45}'
$Script:ClsidPublicBare = 'DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44'
$Script:ExpectedShellAttributesHex = '0xA0000004'
$Script:Findings = [System.Collections.Generic.List[object]]::new()
$Script:LogLines = [System.Collections.Generic.List[string]]::new()

function Write-Diag {
    param([string]$Message)
    Write-Host $Message
    $Script:LogLines.Add($Message)
}

function Add-Finding {
    param(
        [ValidateSet('FAIL', 'WARN', 'OK', 'INFO')]
        [string]$Level,
        [string]$Category,
        [string]$Message,
        [string]$Hint = ''
    )

    $Script:Findings.Add([pscustomobject]@{
            Level    = $Level
            Category = $Category
            Message  = $Message
            Hint     = $Hint
        })

    $color = switch ($Level) {
        'FAIL' { 'Red' }
        'WARN' { 'Yellow' }
        'OK' { 'Green' }
        default { 'Gray' }
    }

    $prefix = "[$Level] $Category`:"
    Write-Host $prefix -ForegroundColor $color -NoNewline
    Write-Host " $Message"
    $Script:LogLines.Add("$prefix $Message")
    if ($Hint) {
        Write-Host "       -> $Hint" -ForegroundColor DarkGray
        $Script:LogLines.Add("       -> $Hint")
    }
}

function Get-RegistryDefaultValue {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Microsoft.Win32.RegistryView]$View = [Microsoft.Win32.RegistryView]::Registry64
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return (Get-ItemProperty -LiteralPath $Path -ErrorAction SilentlyContinue).'(default)'
}

function Get-InprocServer32PathFromView {
    param(
        [Parameter(Mandatory)]
        [string]$Clsid,
        [Microsoft.Win32.RegistryView]$View = [Microsoft.Win32.RegistryView]::Registry64
    )

    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::ClassesRoot, $View)
    $subKey = $baseKey.OpenSubKey("CLSID\$Clsid\InprocServer32")
    if ($null -ne $subKey) {
        try {
            $path = [string]$subKey.GetValue('')
            if ($path) {
                return $path
            }
        }
        finally {
            $subKey.Dispose()
            $baseKey.Dispose()
        }
    }
    else {
        $baseKey.Dispose()
    }

    $lmBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $View)
    $lmSubKey = $lmBase.OpenSubKey("Software\Classes\CLSID\$Clsid\InprocServer32")
    if ($null -eq $lmSubKey) {
        $lmBase.Dispose()
        return $null
    }

    try {
        return [string]$lmSubKey.GetValue('')
    }
    finally {
        $lmSubKey.Dispose()
        $lmBase.Dispose()
    }
}

function Get-InprocServer32Path {
    param(
        [Parameter(Mandatory)]
        [string]$Clsid
    )

    return Get-InprocServer32PathFromView -Clsid $Clsid -View ([Microsoft.Win32.RegistryView]::Registry64)
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Format-Hresult {
    param([int]$Hr)
    return ('0x{0:X8}' -f ($Hr -band 0xFFFFFFFF))
}

function Describe-Hresult {
    param([int]$Hr)

    switch ($Hr -band 0xFFFFFFFF) {
        0x80070483 { return 'ERROR_NOT_FOUND' }
        0x80070057 { return 'E_INVALIDARG' }
        0x80004005 { return 'E_FAIL' }
        0x80040111 { return 'CLASS_E_CLASSNOTAVAILABLE' }
        default { return 'HRESULT failure' }
    }
}

function Get-OptionalRegistryProperty {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Name
    )

    $props = Get-ItemProperty -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $props -or -not ($props.PSObject.Properties.Name -contains $Name)) {
        return $null
    }

    return $props.$Name
}

function Format-HexDword {
    param([object]$Value)

    if ($null -eq $Value) { return '(missing)' }
    if ($Value -is [byte[]]) {
        if ($Value.Length -lt 4) { return '(invalid binary)' }
        $Value = [BitConverter]::ToUInt32($Value, 0)
    }

    return ('0x{0:X8}' -f [uint32]$Value)
}

function Test-ShellFolderAttributes {
    param([object]$Value)

    if ($null -eq $Value) { return $false }
    return ((Format-HexDword $Value) -eq $Script:ExpectedShellAttributesHex)
}

function Get-InstallDirFromRegistry {
    $shellDll = Get-InprocServer32Path -Clsid $Script:ClsidPublic
    if ($shellDll -and (Test-Path -LiteralPath $shellDll)) {
        return (Split-Path -Parent $shellDll)
    }

    $comhost = Get-InprocServer32Path -Clsid $Script:ClsidManaged
    if ($comhost -and (Test-Path -LiteralPath $comhost)) {
        return (Split-Path -Parent $comhost)
    }

    foreach ($candidate in @(
            "${env:ProgramFiles}\Xbox Neighborhood"
            "${env:ProgramFiles(x86)}\Xbox Neighborhood"
        )) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'Rxdk.XbShellExt.Shell.dll')) {
            return $candidate
        }
    }

    return $null
}

function Get-ShortcutDetails {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    try {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut($Path)
        return [pscustomobject]@{
            Path             = $Path
            TargetPath       = [string]$link.TargetPath
            Arguments        = [string]$link.Arguments
            WorkingDirectory = [string]$link.WorkingDirectory
            IconLocation     = [string]$link.IconLocation
            WindowStyle      = [int]$link.WindowStyle
        }
    }
    catch {
        return [pscustomobject]@{
            Path  = $Path
            Error = $_.Exception.Message
        }
    }
}

function Get-InternetShortcutDetails {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $url = $null
    foreach ($line in Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue) {
        if ($line -match '^\s*URL=(.+)\s*$') {
            $url = $Matches[1].Trim()
            break
        }
    }

    return [pscustomobject]@{
        Path = $Path
        Url  = $url
    }
}

function Initialize-ComProbe {
    if (-not ('XbShellExtDiagNative' -as [type])) {
        Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class XbShellExtDiagNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(
        string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr pOuter,
        uint clsctx,
        ref Guid iid,
        out IntPtr ppv);

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll")]
    public static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetClassObjectFn(ref Guid clsid, ref Guid iid, out IntPtr ppv);

    public static int ProbeNamespaceParse(out uint attrs)
    {
        attrs = 0;
        var pidl = IntPtr.Zero;
        try
        {
            return SHParseDisplayName(
                "::{" + "DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44" + "}",
                IntPtr.Zero,
                out pidl,
                0,
                out attrs);
        }
        finally
        {
            if (pidl != IntPtr.Zero)
                ILFree(pidl);
        }
    }

    public static int ProbeCoCreateShellFolder(string clsidText, out IntPtr folder)
    {
        folder = IntPtr.Zero;
        var clsid = new Guid(clsidText);
        var iid = new Guid("000214E6-0000-0000-C000-000000000046"); // IShellFolder
        return CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out folder);
    }

    public static int ProbeDllGetClassObject(string modulePath, string clsidText)
    {
        var module = LoadLibrary(modulePath);
        if (module == IntPtr.Zero)
            return Marshal.GetHRForLastWin32Error();

        try
        {
            var proc = GetProcAddress(module, "DllGetClassObject");
            if (proc == IntPtr.Zero)
                return unchecked((int)0x80004005);

            var fn = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectFn>(proc);
            var clsid = new Guid(clsidText);
            var iidClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
            IntPtr factory;
            return fn(ref clsid, ref iidClassFactory, out factory);
        }
        finally
        {
            FreeLibrary(module);
        }
    }
}
'@
    }

    [void][XbShellExtDiagNative]::CoInitializeEx([IntPtr]::Zero, 2)
}

function Test-DotNetDesktopRuntime {
    $sharedRoot = Join-Path ${env:ProgramFiles} 'dotnet\shared\Microsoft.WindowsDesktop.App'
    if (Test-Path -LiteralPath $sharedRoot) {
        $versions = @(Get-ChildItem -LiteralPath $sharedRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like '8.*' })
        if ($versions.Count -gt 0) {
            return [pscustomobject]@{
                Installed = $true
                Versions  = ($versions.Name -join ', ')
            }
        }
    }

    $regPath = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App'
    if (Test-Path -LiteralPath $regPath) {
        $versions = @(Get-ChildItem -LiteralPath $regPath -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -like '8.*' })
        if ($versions.Count -gt 0) {
            return [pscustomobject]@{
                Installed = $true
                Versions  = ($versions.PSChildName -join ', ')
            }
        }
    }

    return [pscustomobject]@{
        Installed = $false
        Versions  = ''
    }
}

function Get-RecentLogTail {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$Lines = 40
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return @(Get-Content -LiteralPath $Path -Tail $Lines -ErrorAction SilentlyContinue)
}

function Write-LikelyCauses {
    $fails = @($Script:Findings | Where-Object Level -eq 'FAIL')
    $warns = @($Script:Findings | Where-Object Level -eq 'WARN')

    Write-Diag ''
    Write-Diag '=== Likely causes when the shortcut opens Documents / Quick Access ==='
    if ($fails.Count -eq 0 -and $warns.Count -eq 0) {
        Write-Diag 'No obvious registration problems were found.'
        Write-Diag 'If the shortcut still misbehaves, restart Explorer and compare shortcut targets to the expected rundll32 command below.'
    }
    else {
        $rank = 1
        foreach ($item in ($fails + $warns)) {
            if ($item.Hint) {
                Write-Diag ("{0}. [{1}] {2}" -f $rank, $item.Category, $item.Hint)
                $rank++
            }
        }
    }

    Write-Diag ''
    Write-Diag 'Expected desktop/start-menu shortcut:'
    Write-Diag '  Target: C:\Windows\System32\rundll32.exe'
    Write-Diag '  Args:   "<InstallDir>\Rxdk.XbShellExt.Shell.dll",OpenNamespace'
    Write-Diag '  Start in: <InstallDir>'
    Write-Diag ''
    Write-Diag 'Manual open test (run from a NON-admin cmd/PowerShell):'
    Write-Diag '  explorer.exe "shell:::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}"'
}

# --- Main ---

Write-Diag 'Xbox Neighborhood shell extension diagnostic'
Write-Diag ("Time: {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Diag ("Computer: {0}" -f $env:COMPUTERNAME)
Write-Diag ("User: {0}" -f $env:USERNAME)
Write-Diag ("OS: {0}" -f [System.Environment]::OSVersion.VersionString)
Write-Diag ("64-bit OS: {0}" -f [Environment]::Is64BitOperatingSystem)
Write-Diag ("64-bit PowerShell: {0}" -f [Environment]::Is64BitProcess)
Write-Diag ("Elevated session: {0}" -f (Test-IsAdministrator))
Write-Diag ''

if (-not [Environment]::Is64BitOperatingSystem) {
    Add-Finding -Level FAIL -Category 'Platform' -Message '32-bit Windows is not supported.' `
        -Hint 'Install on 64-bit Windows 10 or later.'
}

if (-not [Environment]::Is64BitProcess) {
    Add-Finding -Level WARN -Category 'Platform' -Message 'This diagnostic is running as 32-bit PowerShell.' `
        -Hint 'Re-run with 64-bit PowerShell: %SystemRoot%\SysNative\WindowsPowerShell\v1.0\powershell.exe'
}

if (Test-IsAdministrator) {
    Add-Finding -Level WARN -Category 'Elevation' -Message 'Diagnostic is running elevated.' `
        -Hint 'OpenNamespace refuses elevated processes. Run shortcuts and open tests without admin.'
}

$installDir = Get-InstallDirFromRegistry
if ($installDir) {
    Add-Finding -Level OK -Category 'Install' -Message ("Detected install directory: $installDir")
}
else {
    Add-Finding -Level FAIL -Category 'Install' -Message 'Could not determine Xbox Neighborhood install directory.' `
        -Hint 'Re-run XboxNeighborhood-Setup.exe or verify CC44 InprocServer32 points at Rxdk.XbShellExt.Shell.dll.'
}

$shellRegPath = Get-InprocServer32Path -Clsid $Script:ClsidPublic
$shellRegPathWow = Get-InprocServer32PathFromView -Clsid $Script:ClsidPublic -View ([Microsoft.Win32.RegistryView]::Registry32)
$comhostRegPath = Get-InprocServer32Path -Clsid $Script:ClsidManaged
$comhostRegPathWow = Get-InprocServer32PathFromView -Clsid $Script:ClsidManaged -View ([Microsoft.Win32.RegistryView]::Registry32)

if ($shellRegPath) {
    if (Test-Path -LiteralPath $shellRegPath) {
        if ($shellRegPath -like '*Rxdk.XbShellExt.Shell.dll') {
            Add-Finding -Level OK -Category 'Registry' -Message ("CC44 InprocServer32: $shellRegPath")
        }
        else {
            Add-Finding -Level FAIL -Category 'Registry' -Message ("CC44 points at unexpected module: $shellRegPath") `
                -Hint 'CC44 must reference Rxdk.XbShellExt.Shell.dll (native namespace proxy), not the comhost.'
        }
    }
    else {
        Add-Finding -Level FAIL -Category 'Registry' -Message ("CC44 registered path missing: $shellRegPath") `
            -Hint 'Reinstall or repair registration. A broken path often makes Explorer fall back to Documents/Quick Access.'
    }
}
else {
    Add-Finding -Level FAIL -Category 'Registry' -Message 'CC44 InprocServer32 is not registered in the native 64-bit registry.' `
        -Hint 'Reinstall with a fixed XboxNeighborhood-Setup.exe. Older installers wrote CC44 under Wow6432Node, which 64-bit Explorer ignores.'
}

if ($shellRegPathWow -and -not $shellRegPath) {
    Add-Finding -Level FAIL -Category 'Registry' -Message ("CC44 exists only in 32-bit registry view: $shellRegPathWow") `
        -Hint 'This is the Documents-folder bug: 64-bit Explorer cannot see Wow6432Node InprocServer32 registrations.'
}

if ($comhostRegPath) {
    if (Test-Path -LiteralPath $comhostRegPath) {
        if ($comhostRegPath -like '*Rxdk.XbShellExt.comhost.dll') {
            Add-Finding -Level OK -Category 'Registry' -Message ("CC45 InprocServer32: $comhostRegPath")
        }
        else {
            Add-Finding -Level WARN -Category 'Registry' -Message ("CC45 points at unexpected module: $comhostRegPath") `
                -Hint 'Expected Rxdk.XbShellExt.comhost.dll. Legacy installs may still show xbshlext.dll.'
        }
    }
    else {
        Add-Finding -Level FAIL -Category 'Registry' -Message ("CC45 registered path missing: $comhostRegPath") `
            -Hint 'Run: regsv32 /s "<InstallDir>\Rxdk.XbShellExt.comhost.dll" as admin, then restart Explorer.'
    }
}
else {
    Add-Finding -Level FAIL -Category 'Registry' -Message 'CC45 InprocServer32 is not registered in the native 64-bit registry.' `
        -Hint 'Managed folders will not work. regsv32 the comhost DLL from the install directory.'
}

if ($comhostRegPathWow -and -not $comhostRegPath) {
    Add-Finding -Level WARN -Category 'Registry' -Message ("CC45 exists only in 32-bit registry view: $comhostRegPathWow")
}

$attrsPath = "Registry::HKEY_LOCAL_MACHINE\Software\Classes\CLSID\$Script:ClsidPublic\ShellFolder"
if (-not (Test-Path -LiteralPath $attrsPath)) {
    $attrsPath = "Registry::HKEY_CLASSES_ROOT\CLSID\$Script:ClsidPublic\ShellFolder"
}
$attrs = Get-OptionalRegistryProperty -Path $attrsPath -Name 'Attributes'
if ($null -eq $attrs) {
    Add-Finding -Level FAIL -Category 'Registry' -Message 'CC44 ShellFolder\Attributes is missing.' `
        -Hint 'Must be 0xA0000004. Without SFGAO_FOLDER Explorer treats the namespace as non-folder.'
}
elseif (Test-ShellFolderAttributes $attrs) {
    Add-Finding -Level OK -Category 'Registry' -Message ("CC44 ShellFolder\Attributes = $(Format-HexDword $attrs)")
}
else {
    Add-Finding -Level FAIL -Category 'Registry' -Message ("CC44 ShellFolder\Attributes = $(Format-HexDword $attrs) (expected 0xA0000004)") `
        -Hint 'Wrong attributes are a common cause of "no app associated" or Explorer opening the wrong location.'
}

foreach ($badKey in @(
        "Registry::HKEY_CLASSES_ROOT\CLSID\$Script:ClsidPublic\shell\open\command"
        'Registry::HKEY_CLASSES_ROOT\Shellext.XboxFolder.1\shell\open\command'
    )) {
    if (Test-Path -LiteralPath $badKey) {
        $command = Get-RegistryDefaultValue -Path $badKey
        Add-Finding -Level FAIL -Category 'Registry' -Message ("Erroneous shell open verb: $badKey") `
            -Hint "Remove this key. Value was: $command. It hijacks namespace open and can spin Explorer or open the wrong folder."
    }
}

$approvedPath = 'Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved'
$approved = (Get-ItemProperty -LiteralPath $approvedPath -ErrorAction SilentlyContinue).$Script:ClsidPublicBare
if ($approved) {
    Add-Finding -Level OK -Category 'Registry' -Message 'Shell Extensions\Approved contains CC44.'
}
else {
    Add-Finding -Level WARN -Category 'Registry' -Message 'CC44 is not listed under Shell Extensions\Approved.' `
        -Hint 'Some Explorer configurations require this entry for namespace extensions.'
}

$navPane = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -ErrorAction SilentlyContinue).NavPaneShowAllFolders
if ($navPane -eq 1) {
    Add-Finding -Level OK -Category 'Explorer' -Message 'NavPaneShowAllFolders = 1'
}
else {
    Add-Finding -Level WARN -Category 'Explorer' -Message ("NavPaneShowAllFolders = $(if ($null -eq $navPane) { '(missing)' } else { $navPane })") `
        -Hint 'Set HKCU\...\Explorer\Advanced\NavPaneShowAllFolders to 1 on Windows 10.'
}

$userShellFolder = "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\$Script:ClsidPublic\ShellFolder"
$userAttrs = Get-OptionalRegistryProperty -Path $userShellFolder -Name 'Attributes'
if ($null -ne $userAttrs -and ($userAttrs -band 0x100000)) {
    Add-Finding -Level WARN -Category 'Explorer' -Message 'Per-user ShellFolder override hides the namespace (SFGAO_NONENUMERATED).' `
        -Hint "Delete HKCU\...\Explorer\CLSID\$Script:ClsidPublic\ShellFolder and restart Explorer."
}

if ($installDir) {
    $requiredFiles = @(
        'Rxdk.XbShellExt.Shell.dll'
        'Rxdk.XbShellExt.comhost.dll'
        'Rxdk.XbShellExt.dll'
        'Rxdk.XbShellExt.UI.dll'
        'RXDKNeighborhood.Core.dll'
        'Rxdk.Xbdm.KitServices.dll'
        'Rxdk.Xbdm.Managed.dll'
        'Rxdk.Xbdm.Abstractions.dll'
        'Rxdk.XbShellExt.runtimeconfig.json'
        'Rxdk.XbShellExt.deps.json'
        'xbox.ico'
    )

    foreach ($fileName in $requiredFiles) {
        $fullPath = Join-Path $installDir $fileName
        if (Test-Path -LiteralPath $fullPath) {
            Add-Finding -Level OK -Category 'Payload' -Message "Present: $fileName"
        }
        else {
            $level = if ($fileName -in @('Rxdk.XbShellExt.Shell.dll', 'Rxdk.XbShellExt.comhost.dll')) { 'FAIL' } else { 'WARN' }
            Add-Finding -Level $level -Category 'Payload' -Message "Missing: $fullPath" `
                -Hint 'Reinstall XboxNeighborhood-Setup.exe to restore the payload.'
        }
    }
}

$dotnet = Test-DotNetDesktopRuntime
if ($dotnet.Installed) {
    Add-Finding -Level OK -Category 'Runtime' -Message (".NET 8 Desktop Runtime found: $($dotnet.Versions)")
}
else {
    Add-Finding -Level WARN -Category 'Runtime' -Message '.NET 8 Desktop Runtime (x64) not detected.' `
        -Hint 'The namespace may open but managed views/dialogs fail. The installer normally installs this prerequisite.'
}

Write-Diag ''
Write-Diag '=== Shortcuts ==='

$shortcutPaths = @(
    (Join-Path $env:PUBLIC 'Desktop\Xbox Neighborhood.lnk')
    (Join-Path $env:USERPROFILE 'Desktop\Xbox Neighborhood.lnk')
)

$programs = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
if (Test-Path -LiteralPath $programs) {
    $shortcutPaths += @(Get-ChildItem -LiteralPath $programs -Recurse -Filter 'Xbox Neighborhood*.lnk' -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName)
}

$shortcutPaths = @($shortcutPaths | Where-Object { $_ } | Select-Object -Unique)
$foundShortcut = $false

foreach ($shortcutPath in $shortcutPaths) {
    if (-not (Test-Path -LiteralPath $shortcutPath)) { continue }
    $foundShortcut = $true
    $details = Get-ShortcutDetails -Path $shortcutPath

    Write-Diag "Shortcut: $shortcutPath"
    if ($details.PSObject.Properties['Error']) {
        Add-Finding -Level WARN -Category 'Shortcut' -Message ("Could not read shortcut: $($details.Error)")
        continue
    }

    Write-Diag ("  Target: $($details.TargetPath)")
    Write-Diag ("  Args:   $($details.Arguments)")
    Write-Diag ("  Start:  $($details.WorkingDirectory)")

    $targetOk = $details.TargetPath -like '*\System32\rundll32.exe' -or $details.TargetPath -like '*\SysWOW64\rundll32.exe'
    $argsOk = $details.Arguments -match 'Rxdk\.XbShellExt\.Shell\.dll' -and $details.Arguments -match 'OpenNamespace'

    if (-not $targetOk) {
        Add-Finding -Level FAIL -Category 'Shortcut' -Message "Wrong target in $shortcutPath" `
            -Hint 'Target must be System32\rundll32.exe. A bare explorer.exe shortcut opens Documents/Quick Access.'
    }
    elseif (-not $argsOk) {
        Add-Finding -Level FAIL -Category 'Shortcut' -Message "Wrong arguments in $shortcutPath" `
            -Hint 'Arguments must be "<InstallDir>\Rxdk.XbShellExt.Shell.dll",OpenNamespace'
    }
    else {
        if ($details.Arguments -match '"([^"]+Rxdk\.XbShellExt\.Shell\.dll)"') {
            $dllPath = $Matches[1]
            if (-not (Test-Path -LiteralPath $dllPath)) {
                Add-Finding -Level FAIL -Category 'Shortcut' -Message "Shortcut Shell.dll path missing: $dllPath" `
                    -Hint 'Stale shortcut after move/uninstall. Reinstall or recreate the shortcut.'
            }
            else {
                Add-Finding -Level OK -Category 'Shortcut' -Message "Shortcut looks correct: $shortcutPath"
            }
        }
        else {
            Add-Finding -Level WARN -Category 'Shortcut' -Message "Could not parse Shell.dll path from shortcut arguments."
        }
    }
}

if (-not $foundShortcut) {
    Add-Finding -Level WARN -Category 'Shortcut' -Message 'No Xbox Neighborhood .lnk shortcut found on desktop or start menu.' `
        -Hint 'Reinstall or manually create a shortcut to rundll32.exe with OpenNamespace.'
}

foreach ($urlPath in @(
        (Join-Path $env:PUBLIC 'Desktop\Xbox Neighborhood.url')
        (Join-Path $env:USERPROFILE 'Desktop\Xbox Neighborhood.url')
    )) {
    if (-not (Test-Path -LiteralPath $urlPath)) { continue }
    $url = Get-InternetShortcutDetails -Path $urlPath
    Add-Finding -Level WARN -Category 'Shortcut' -Message ("Legacy Internet shortcut found: $urlPath -> $($url.Url)") `
        -Hint 'Old installs used .url files. Remove it and use the .lnk shortcut from the current installer.'
}

Write-Diag ''
Write-Diag '=== COM / namespace probes ==='

try {
    Initialize-ComProbe

    $parseAttrs = [uint32]0
    $hrParse = [XbShellExtDiagNative]::ProbeNamespaceParse([ref]$parseAttrs)
    if ($hrParse -ge 0) {
        Add-Finding -Level OK -Category 'COM' -Message ("SHParseDisplayName(shell:::{CC44}) succeeded (attrs=$(Format-HexDword $parseAttrs))")
    }
    else {
        Add-Finding -Level FAIL -Category 'COM' -Message ('SHParseDisplayName failed: ' + (Format-Hresult $hrParse) + ' (' + (Describe-Hresult $hrParse) + ')') `
            -Hint 'Explorer cannot resolve shell:::{CC44}. CC44 InprocServer32 and ShellFolder\Attributes must exist in the native 64-bit registry.'
    }

    if ($shellRegPath -and (Test-Path -LiteralPath $shellRegPath)) {
        $folderPtr = [IntPtr]::Zero
        $hrCo = [XbShellExtDiagNative]::ProbeCoCreateShellFolder($Script:ClsidPublic, [ref]$folderPtr)
        if ($hrCo -ge 0 -and $folderPtr -ne [IntPtr]::Zero) {
            Add-Finding -Level OK -Category 'COM' -Message ("CoCreateInstance CC44 (IShellFolder) succeeded: $(Format-Hresult $hrCo)")
            [void][System.Runtime.InteropServices.Marshal]::Release($folderPtr)
        }
        else {
            Add-Finding -Level FAIL -Category 'COM' -Message ("CoCreateInstance CC44 failed: $(Format-Hresult $hrCo)") `
                -Hint 'Native Shell.dll cannot be loaded. Check VC++ runtime, blocked DLL, or wrong architecture.'
        }

        $hrGco = [XbShellExtDiagNative]::ProbeDllGetClassObject($shellRegPath, $Script:ClsidPublic)
        if ($hrGco -ge 0) {
            Add-Finding -Level OK -Category 'COM' -Message ("DllGetClassObject on Shell.dll succeeded: $(Format-Hresult $hrGco)")
        }
        else {
            Add-Finding -Level FAIL -Category 'COM' -Message ("DllGetClassObject on Shell.dll failed: $(Format-Hresult $hrGco)") `
                -Hint 'Shell.dll failed to load or export DllGetClassObject. Reinstall the extension.'
        }
    }

    if ($comhostRegPath -and (Test-Path -LiteralPath $comhostRegPath)) {
        $hrGcoManaged = [XbShellExtDiagNative]::ProbeDllGetClassObject($comhostRegPath, $Script:ClsidManaged)
        if ($hrGcoManaged -ge 0) {
            Add-Finding -Level OK -Category 'COM' -Message ("DllGetClassObject on comhost succeeded: $(Format-Hresult $hrGcoManaged)")
        }
        else {
            Add-Finding -Level FAIL -Category 'COM' -Message ("DllGetClassObject on comhost failed: $(Format-Hresult $hrGcoManaged)") `
                -Hint 'Usually missing .NET 8 Desktop Runtime or missing dependency DLLs beside comhost.'
        }
    }
}
catch {
    Add-Finding -Level WARN -Category 'COM' -Message ("COM probe unavailable: $($_.Exception.Message)")
}

Write-Diag ''
Write-Diag '=== Trace logs (recent tail) ==='

$logDir = Join-Path ${env:ProgramData} 'Xbox Neighborhood\Logs'
foreach ($logName in @('xb-shlext.log', 'xb-shlext-mgd.log')) {
    $logPath = Join-Path $logDir $logName
    Write-Diag "Log: $logPath"
    $tail = Get-RecentLogTail -Path $logPath
    if ($null -eq $tail) {
        Write-Diag '  (not found)'
    }
    else {
        foreach ($line in $tail) { Write-Diag "  $line" }
    }
}

if ($TryOpen) {
    Write-Diag ''
    Write-Diag '=== Open test ==='
    if (Test-IsAdministrator) {
        Add-Finding -Level WARN -Category 'OpenTest' -Message 'Skipped OpenNamespace test because session is elevated.'
    }
    elseif ($shellRegPath -and (Test-Path -LiteralPath $shellRegPath)) {
        Write-Diag 'Launching rundll32 OpenNamespace...'
        Start-Process -FilePath "$env:SystemRoot\System32\rundll32.exe" -ArgumentList @(
            $shellRegPath,
            'OpenNamespace'
        )
        Add-Finding -Level INFO -Category 'OpenTest' -Message 'OpenNamespace launched. Check whether Xbox Neighborhood opened (not Documents).'
    }
    else {
        Add-Finding -Level FAIL -Category 'OpenTest' -Message 'Cannot run OpenNamespace - Shell.dll path unavailable.'
    }
}

Write-Diag ''
Write-Diag '=== Summary ==='
$failCount = @($Script:Findings | Where-Object Level -eq 'FAIL').Count
$warnCount = @($Script:Findings | Where-Object Level -eq 'WARN').Count
$okCount = @($Script:Findings | Where-Object Level -eq 'OK').Count
Write-Diag ("FAIL: $failCount  WARN: $warnCount  OK: $okCount")

Write-LikelyCauses

if ($SaveReport) {
    $reportDir = Join-Path ${env:ProgramData} 'Xbox Neighborhood\Logs'
    if (-not (Test-Path -LiteralPath $reportDir)) {
        New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $reportPath = Join-Path $reportDir "diagnostic-$stamp.txt"
    $Script:LogLines | Set-Content -LiteralPath $reportPath -Encoding UTF8

    $desktopCopy = Join-Path $env:USERPROFILE "Desktop\XboxNeighborhood-diagnostic-$stamp.txt"
    Copy-Item -LiteralPath $reportPath -Destination $desktopCopy -Force

    Write-Diag ''
    Write-Diag "Report saved:"
    Write-Diag "  $reportPath"
    Write-Diag "  $desktopCopy"
}

if ($failCount -gt 0) { exit 1 }
exit 0
