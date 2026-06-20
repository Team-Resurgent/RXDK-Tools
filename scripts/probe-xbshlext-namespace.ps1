# Probe shell namespace registration and COM activation.
param(
    [string]$Clsid = '{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}'
)

$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force

Write-Host '=== Registry ==='
$regPath = "HKCR:\CLSID\$Clsid"
if (Test-Path $regPath) {
    Get-ItemProperty $regPath | Format-List
    $sf = Join-Path $regPath 'ShellFolder'
    if (Test-Path $sf) {
        Write-Host "ShellFolder attributes: $((Get-ItemProperty $sf).Attributes)"
    } else {
        Write-Host 'ShellFolder subkey: MISSING'
    }
    $inproc = Join-Path $regPath 'InprocServer32'
    if (Test-Path $inproc) {
        Write-Host "InprocServer32: $((Get-ItemProperty $inproc).'(default)')"
        Write-Host "ThreadingModel: $((Get-ItemProperty $inproc).ThreadingModel)"
    }
} else {
    Write-Host "CLSID key missing: $regPath"
}

$approved = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved'
if (Test-Path $approved) {
    $val = (Get-ItemProperty $approved -ErrorAction SilentlyContinue).$Clsid.Trim('{}')
    $val2 = (Get-ItemProperty $approved -ErrorAction SilentlyContinue).$Clsid
    Write-Host "Approved ($Clsid): $(if ($val2) { $val2 } else { '(missing)' })"
}

Write-Host ''
Write-Host '=== COM activation ==='
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class ShellProbe {
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    public const int CLSCTX_INPROC_SERVER = 1;
    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellFolder {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, out uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppEnumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr prgfInOut, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistFolder {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int Initialize(IntPtr pidl);
    }
}
"@

function Format-Hr($hr) {
    $bytes = [BitConverter]::GetBytes([int32]$hr)
    '0x{0:x8}' -f [BitConverter]::ToUInt32($bytes, 0)
}

$guid = [Guid]$Clsid
$shellFolderGuid = [Guid]'000214E6-0000-0000-C000-000000000046'
$shellViewGuid = [Guid]'000214E1-0000-0000-C000-000000000046'
$ptr = [IntPtr]::Zero
$hr = [ShellProbe]::CoCreateInstance([ref]$guid, [IntPtr]::Zero, [ShellProbe]::CLSCTX_INPROC_SERVER, [ref]$shellFolderGuid, [ref]$ptr)
Write-Host "CoCreateInstance(IShellFolder): $(Format-Hr $hr) ptr=$ptr"
if ($hr -ge 0 -and $ptr -ne [IntPtr]::Zero) {
    $folder = [Runtime.InteropServices.Marshal]::GetObjectForIUnknown($ptr)
    $parseName = "::" + $Clsid
    $pidl = [IntPtr]::Zero
    $attrs = 0
    $hr2 = [ShellProbe]::SHParseDisplayName($parseName, [IntPtr]::Zero, [ref]$pidl, 0, [ref]$attrs)
    Write-Host "SHParseDisplayName('$parseName'): $(Format-Hr $hr2) pidl=$pidl"
    if ($hr2 -ge 0 -and $pidl -ne [IntPtr]::Zero) {
        $persist = $folder -as [ShellProbe+IPersistFolder]
        if ($null -ne $persist) {
            $hr3 = $persist.Initialize($pidl)
            Write-Host "IPersistFolder.Initialize: $(Format-Hr $hr3)"
        } else {
            Write-Host 'IPersistFolder QI: failed'
        }

        $sf = $folder -as [ShellProbe+IShellFolder]
        if ($null -ne $sf) {
            foreach ($viewName in @(
                    @{ Name = 'IShellView'; Guid = [Guid]'000214E1-0000-0000-C000-000000000046' },
                    @{ Name = 'IShellView2'; Guid = [Guid]'000214E3-0000-0000-C000-000000000046' }
                )) {
                $viewPtr = [IntPtr]::Zero
                $riid = $viewName.Guid
                $hr4 = $sf.CreateViewObject([IntPtr]::Zero, [ref]$riid, [ref]$viewPtr)
                Write-Host "$($viewName.Name) CreateViewObject: $(Format-Hr $hr4) ptr=$viewPtr"
                if ($viewPtr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::Release($viewPtr) | Out-Null }
            }
        }
        [ShellProbe]::ILFree($pidl) | Out-Null
    }
    [Runtime.InteropServices.Marshal]::Release($ptr) | Out-Null
}

Write-Host ''
Write-Host '=== Explorer argument note ==='
Write-Host "In PowerShell, quote the namespace: explorer.exe 'shell:::$Clsid'"
