# Deep probe: CreateViewObject, EnumObjects, registry (64-bit view).
param(
    [string]$Clsid = '{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}'
)

$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force

function Format-Hr($hr) {
    $bytes = [BitConverter]::GetBytes([int32]$hr)
    '0x{0:x8}' -f [BitConverter]::ToUInt32($bytes, 0)
}

$registered = Get-XbShellExtRegisteredPath
Write-Host "Registered DLL: $registered"
if ($registered) {
    $item = Get-Item -LiteralPath $registered
    Write-Host "  LastWriteTime: $($item.LastWriteTime)"
    $managed = Join-Path (Split-Path $registered) 'Rxdk.XbShellExt.dll'
    if (Test-Path -LiteralPath $managed) {
        Write-Host "  Managed DLL: $((Get-Item -LiteralPath $managed).LastWriteTime)"
    }
}

$baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::ClassesRoot, [Microsoft.Win32.RegistryView]::Registry64)
$clsidKey = $baseKey.OpenSubKey("CLSID\$Clsid")
if ($null -eq $clsidKey) {
    Write-Host 'CLSID key missing in 64-bit HKCR'
} else {
    Write-Host "CLSID default: $($clsidKey.GetValue(''))"
    $sf = $clsidKey.OpenSubKey('ShellFolder')
    if ($sf) { Write-Host "ShellFolder Attributes: 0x$('{0:x8}' -f [uint32][int32]$sf.GetValue('Attributes'))" }
    $inproc = $clsidKey.OpenSubKey('InprocServer32')
    if ($inproc) {
        Write-Host "InprocServer32: $($inproc.GetValue(''))"
        Write-Host "ThreadingModel: $($inproc.GetValue('ThreadingModel'))"
    }
    $clsidKey.Dispose()
}
$baseKey.Dispose()

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DeepProbe {
    public const int CLSCTX_INPROC_SERVER = 1;

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellFolder {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, out uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, [MarshalAs(UnmanagedType.Interface)] out object ppEnumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr prgfInOut, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumIDList {
        [PreserveSig] int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistFolder {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int Initialize(IntPtr pidl);
    }
}
"@

$guid = [Guid]$Clsid
$sfGuid = [Guid]'000214E6-0000-0000-C000-000000000046'
$ptr = [IntPtr]::Zero
$hr = [DeepProbe]::CoCreateInstance([ref]$guid, [IntPtr]::Zero, [DeepProbe]::CLSCTX_INPROC_SERVER, [ref]$sfGuid, [ref]$ptr)
Write-Host ''
Write-Host "CoCreateInstance: $(Format-Hr $hr)"
if ($hr -lt 0) { exit 1 }

$folder = [Runtime.InteropServices.Marshal]::GetObjectForIUnknown($ptr)
$sf = [DeepProbe+IShellFolder]$folder

$enumObj = $null
$hrEnum = $sf.EnumObjects([IntPtr]::Zero, 0, [ref]$enumObj)
Write-Host "EnumObjects: $(Format-Hr $hrEnum) enum=$enumObj"
if ($enumObj -ne $null) {
    $enum = [DeepProbe+IEnumIDList]$enumObj
    $itemPidl = [IntPtr]::Zero
    $fetched = [uint32]0
    $hrNext = $enum.Next(1, [ref]$itemPidl, [ref]$fetched)
    Write-Host "EnumObjects.Next: $(Format-Hr $hrNext) fetched=$fetched pidl=$itemPidl"
    if ($itemPidl -ne [IntPtr]::Zero) {
        [DeepProbe]::CoCreateInstance([ref]$guid, [IntPtr]::Zero, [DeepProbe]::CLSCTX_INPROC_SERVER, [ref]$sfGuid, [ref]$null) | Out-Null
    }
}

foreach ($pair in @(
        @{ Name = 'IShellView'; Id = '000214E1-0000-0000-C000-000000000046' },
        @{ Name = 'IShellView2'; Id = '000214E3-0000-0000-C000-000000000046' }
    )) {
    $riid = [Guid]$pair.Id
    $view = [IntPtr]::Zero
    $hrView = $sf.CreateViewObject([IntPtr]::Zero, [ref]$riid, [ref]$view)
    Write-Host "$($pair.Name) CreateViewObject: $(Format-Hr $hrView) ptr=$view"
    if ($view -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::Release($view) | Out-Null }
}

[Runtime.InteropServices.Marshal]::Release($ptr) | Out-Null

Write-Host ''
Write-Host 'Try opening (quoted):'
Write-Host "  explorer.exe 'shell:::$Clsid'"
