param(
    [string]$Dir,
    [switch]$OnlyComHost
)

if ([string]::IsNullOrWhiteSpace($Dir)) {
    Import-Module (Join-Path $PSScriptRoot 'lib\XbShellExtDev.psm1') -Force
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $Dir = Get-XbShellExtDefaultStageDir -RepoRoot $repoRoot
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeLoad {
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);
    [DllImport("kernel32", SetLastError = true)]
    public static extern bool SetDllDirectoryW(string lpPathName);
    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int DllRegisterServerFn();
}
"@

function Format-HResult($hr) {
    $bytes = [BitConverter]::GetBytes([int32]$hr)
    ('0x{0:x8}' -f [BitConverter]::ToUInt32($bytes, 0))
}

function Get-HResultHint($hr) {
    switch ([int32]$hr) {
        0 { return 'S_OK' }
        -2147024891 { return 'E_ACCESSDENIED - run from an elevated PowerShell (register-xbshlext-dev.ps1)' }
        default { return $null }
    }
}

function Test-Load([string]$name) {
    [void][NativeLoad]::SetDllDirectoryW($Dir)
    $path = Join-Path $Dir $name
    $h = [NativeLoad]::LoadLibraryExW($path, [IntPtr]::Zero, 8)
    if ($h -eq [IntPtr]::Zero) {
        $err = [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
        Write-Host "FAIL $name : $($err.NativeErrorCode) $($err.Message)"
        return $null
    }
    Write-Host "OK   $name"
    return $h
}

$targets = if ($OnlyComHost) {
    @('Rxdk.XbShellExt.comhost.dll')
}
else {
    @(
        'Rxdk.XbShellExt.dll',
        'Rxdk.XbShellExt.comhost.dll'
    )
}

foreach ($dll in $targets) {
    $h = Test-Load $dll
    if ($dll -eq 'Rxdk.XbShellExt.comhost.dll' -and $null -ne $h) {
        $proc = [NativeLoad]::GetProcAddress($h, 'DllRegisterServer')
        if ($proc -ne [IntPtr]::Zero) {
            $fn = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($proc, [NativeLoad+DllRegisterServerFn])
            $hr = $fn.Invoke()
            $hrText = Format-HResult $hr
            $hint = Get-HResultHint $hr
            Write-Host "     DllRegisterServer => $hrText$(if ($hint) { " ($hint)" })"
        }
    }
}
