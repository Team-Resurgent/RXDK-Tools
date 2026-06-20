# Calls DllRegisterServer on a staged xbshlext and prints the HRESULT.
param(
    [string]$DllPath = 'd:\Git\XboxNeighborhood\out\dev\xbshlext\Rxdk.XbShellExt.comhost.dll'
)

$ErrorActionPreference = 'Stop'
$Dir = Split-Path -Parent $DllPath

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeReg {
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);
    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    [DllImport("kernel32", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr hModule);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int DllRegisterServerFn();
}
"@

$LOAD_WITH_ALTERED_SEARCH_PATH = 8
$h = [NativeReg]::LoadLibraryExW($DllPath, [IntPtr]::Zero, $LOAD_WITH_ALTERED_SEARCH_PATH)
if ($h -eq [IntPtr]::Zero) {
    $err = [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
    throw "LoadLibraryEx failed: $($err.Message)"
}

try {
    $proc = [NativeReg]::GetProcAddress($h, 'DllRegisterServer')
    if ($proc -eq [IntPtr]::Zero) {
        throw 'GetProcAddress(DllRegisterServer) failed.'
    }

    $fn = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($proc, [NativeReg+DllRegisterServerFn])
    $hr = $fn.Invoke()
    Write-Host ("DllRegisterServer HRESULT: {0}" -f ('0x{0:x8}' -f ([uint32][int32]$hr)))
    exit $(if ($hr -ge 0) { 0 } else { 1 })
}
finally {
    [void][NativeReg]::FreeLibrary($h)
}
