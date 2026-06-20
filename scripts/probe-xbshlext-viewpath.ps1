# Quick hang test: CC44 view path without loading comhost for icons/enum.
param(
    [string]$Clsid = '{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force

$registered = Get-XbShellExtRegisteredPath
Write-Host "Registered Shell.dll: $registered"
if (-not $registered) { throw 'Shell extension not registered.' }

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class ViewPathProbe {
    public const int CLSCTX_INPROC_SERVER = 1;
    [DllImport("ole32.dll")] public static extern int CoInitializeEx(IntPtr r, int f);
    [DllImport("ole32.dll")] public static extern void CoUninitialize();
    [DllImport("ole32.dll")] public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] public static extern void ILFree(IntPtr pidl);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int InitializeFn(IntPtr self, IntPtr pidl);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int EnumObjectsFn(IntPtr self, IntPtr hwnd, uint flags, out IntPtr enumIdList);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetUiObjectOfFn(IntPtr self, IntPtr hwnd, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgf, out IntPtr ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int EnumNextFn(IntPtr self, uint celt, out IntPtr pidl, out uint fetched);

    public static int CallVtable(IntPtr obj, int slot, out IntPtr fn) {
        fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), IntPtr.Size * slot);
        return 0;
    }
}
"@

[ViewPathProbe]::CoInitializeEx([IntPtr]::Zero, 2) | Out-Null
try {
    $clsid = [Guid]$Clsid
    $iidSf = [Guid]'000214E6-0000-0000-C000-000000000046'
    $iidExtract = [Guid]'000214EB-0000-0000-C000-000000000046'
    $iidEnum = [Guid]'000214F2-0000-0000-C000-000000000046'

    $hr = [ViewPathProbe]::CoCreateInstance([ref]$clsid, [IntPtr]::Zero, 1, [ref]$iidSf, [ref]$folder)
    Write-Host "CoCreate CC44: 0x$("{0:X8}" -f $hr) folder=0x$folder.ToString('X')"
    if ($hr -lt 0) { exit 1 }

    $attrs = 0u
    $hr = [ViewPathProbe]::SHParseDisplayName("::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}", [IntPtr]::Zero, [ref]$pidl, 0, [ref]$attrs)
    Write-Host "SHParseDisplayName: 0x$("{0:X8}" -f $hr)"
    if ($hr -lt 0) { exit 1 }

    $vt = [IntPtr]::Zero
    [void][ViewPathProbe]::CallVtable($folder, 4, [ref]$vt)
    $init = [Marshal]::GetDelegateForFunctionPointer[ViewPathProbe+InitializeFn]($vt)
    $hr = $init.Invoke($folder, $pidl)
    Write-Host "Initialize: 0x$("{0:X8}" -f $hr)"

    [void][ViewPathProbe]::CallVtable($folder, 5, [ref]$vt)
    $enumObj = [Marshal]::GetDelegateForFunctionPointer[ViewPathProbe+EnumObjectsFn]($vt)
    $hr = $enumObj.Invoke($folder, [IntPtr]::Zero, 0x0060u, [ref]$enumPtr)
    Write-Host "EnumObjects: 0x$("{0:X8}" -f $hr) enum=0x$enumPtr.ToString('X')"

    if ($enumPtr -ne [IntPtr]::Zero) {
        [void][ViewPathProbe]::CallVtable($enumPtr, 3, [ref]$vt)
        $next = [Marshal]::GetDelegateForFunctionPointer[ViewPathProbe+EnumNextFn]($vt)
        $count = 0
        while ($true) {
            $item = [IntPtr]::Zero; $fetched = 0u
            $hrN = $next.Invoke($enumPtr, 1, [ref]$item, [ref]$fetched)
            if ($hrN -ne 0 -or $fetched -ne 1) { break }
            $count++
            Write-Host "  enum item $count pidl=0x$($item.ToString('X'))"
            $apidl = [Marshal]::AllocHGlobal([IntPtr]::Size)
            [Marshal]::WriteIntPtr($apidl, $item)
            [void][ViewPathProbe]::CallVtable($folder, 11, [ref]$vt)
            $getUi = [Marshal]::GetDelegateForFunctionPointer[ViewPathProbe+GetUiObjectOfFn]($vt)
            $icon = [IntPtr]::Zero
            $hrI = $getUi.Invoke($folder, [IntPtr]::Zero, 1, $apidl, [ref]$iidExtract, [IntPtr]::Zero, [ref]$icon)
            Write-Host "  IExtractIcon: 0x$("{0:X8}" -f $hrI) ptr=0x$($icon.ToString('X'))"
            [Marshal]::FreeHGlobal($apidl)
            if ($icon -ne [IntPtr]::Zero) { [Marshal]::Release($icon) }
        }
    }

    Write-Host 'Done (no hang).'
}
finally {
    if ($pidl -ne [IntPtr]::Zero) { [ViewPathProbe]::ILFree($pidl) }
    if ($folder -ne [IntPtr]::Zero) { [Marshal]::Release($folder) }
    [ViewPathProbe]::CoUninitialize()
}
