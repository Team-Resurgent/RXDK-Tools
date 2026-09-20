namespace Rxdk.Engine.Platform;

/// <summary>
/// Platform paths for host tools, the staged SDK, and the managed Zig install. Ported from the
/// path logic in RXDK-VSCode (bridgePath.ts, hostTools.ts, sdkStaging.ts, zigRuntime.ts). Now that
/// this engine is shared by both IDEs -- VS20XX (Windows) and VS Code (Windows/Linux/macOS) -- it is
/// cross-platform: the executable suffix and RID follow the OS, and the …/RXDK roots honor the
/// RXDK_STAGED_* env overrides the caller sets for its per-platform layout.
/// </summary>
public static class RxdkPaths
{
    /// <summary>Host-tools RID for the current OS/arch.</summary>
    public static string ToolRid =>
        System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier is var rid && rid.StartsWith("win")
            ? "win-x64"
            : OperatingSystem.IsMacOS()
                ? (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64")
                : (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "linux-arm64" : "linux-x64");

    /// <summary>Executable name for a host tool: adds the .exe suffix on Windows only.</summary>
    public static string HostToolExecutableName(string baseName) =>
        OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName;

    /// <summary>
    /// Persistent RXDK data root, matching RXDK-VSCode:
    /// Windows <c>%ProgramData%\RXDK</c>, macOS <c>~/Library/Application Support/RXDK</c>,
    /// Linux <c>$XDG_DATA_HOME/rxdk</c> (default <c>~/.local/share/rxdk</c>).
    /// </summary>
    public static string RxdkDataRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            // RXDK env var is a pure OVERRIDE (CI / advanced installs); normally unset.
            var over = Environment.GetEnvironmentVariable("RXDK");
            if (!string.IsNullOrWhiteSpace(over))
                return over.Trim();
            // A custom install path chosen in the standalone installer, recorded in the registry
            // (Windows only). VS Code-only / plain-VSIX installs never write it, so they fall
            // through to the ProgramData default exactly as before.
            var reg = RegistryInstallPath();
            if (!string.IsNullOrEmpty(reg))
                return reg!;
            var programData = Environment.GetEnvironmentVariable("ProgramData");
            if (string.IsNullOrEmpty(programData))
                programData = @"C:\ProgramData";
            return Path.Combine(programData, "RXDK");
        }
        if (OperatingSystem.IsMacOS())
            return Path.Combine(HomeDirectory(), "Library", "Application Support", "RXDK");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(xdg))
            xdg = Path.Combine(HomeDirectory(), ".local", "share");
        return Path.Combine(xdg, "rxdk");
    }

    /// <summary>
    /// The RXDK install path the standalone installer recorded, or null. Reads
    /// <c>HKLM\SOFTWARE\TeamResurgent\RXDK\InstallPath</c>. The installer writes BOTH registry
    /// views, and 32-bit readers (incl. MSBuild's <c>$(Registry:…)</c>) resolve through
    /// WOW6432Node, so try the 32-bit view first, then the 64-bit view. Read via a direct
    /// advapi32 P/Invoke rather than Microsoft.Win32.Registry so it needs no extra assembly in the
    /// framework-dependent engine (that package is not guaranteed in the net8 runtime users get).
    /// Windows only; the caller is already IsWindows()-guarded.
    /// </summary>
    private static string? RegistryInstallPath()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        const uint RRF_RT_REG_SZ = 0x00000002;
        const uint RRF_SUBKEY_WOW6464KEY = 0x00010000;
        const uint RRF_SUBKEY_WOW6432KEY = 0x00020000;
        var HKLM = unchecked((nint)0x80000002);
        foreach (var view in new[] { RRF_SUBKEY_WOW6432KEY, RRF_SUBKEY_WOW6464KEY })
        {
            try
            {
                uint cb = 0;
                if (RegGetValueW(HKLM, @"SOFTWARE\TeamResurgent\RXDK", "InstallPath",
                                 RRF_RT_REG_SZ | view, out _, null, ref cb) != 0 || cb == 0)
                    continue;
                var buf = new byte[cb];
                if (RegGetValueW(HKLM, @"SOFTWARE\TeamResurgent\RXDK", "InstallPath",
                                 RRF_RT_REG_SZ | view, out _, buf, ref cb) != 0)
                    continue;
                // cb includes the terminating NUL; decode as UTF-16 and trim it.
                var s = System.Text.Encoding.Unicode.GetString(buf, 0, (int)cb).TrimEnd('\0').Trim();
                if (!string.IsNullOrEmpty(s) && Directory.Exists(s))
                    return s;
            }
            catch { /* registry unavailable / access denied -> fall through */ }
        }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "RegGetValueW")]
    private static extern int RegGetValueW(nint hkey, string subKey, string value, uint flags,
                                           out uint type, byte[]? data, ref uint cbData);

    private static string HomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home)
            ? (Environment.GetEnvironmentVariable("HOME") ?? "/")
            : home;
    }

    private static string LocalAppData() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // ---- Staged host tools (…/RXDK/tools) ----

    public static string GetDefaultStagedToolsRoot() =>
        Path.Combine(RxdkDataRoot(), "tools");

    /// <summary>Effective staged tools root, honoring the RXDK_STAGED_TOOLS override.</summary>
    public static string GetStagedToolsRoot() =>
        EnvOverride("RXDK_STAGED_TOOLS") ?? GetDefaultStagedToolsRoot();

    /// <summary>Absolute path to a host tool in the staged tools root (may not exist yet).</summary>
    public static string ResolveHostTool(string baseName) =>
        Path.Combine(GetStagedToolsRoot(), HostToolExecutableName(baseName));

    // ---- Staged SDK (headers + libs, …/RXDK/sdk) ----

    public static string GetDefaultStagedSdkRoot() =>
        Path.Combine(RxdkDataRoot(), "sdk");

    /// <summary>Effective staged SDK root, honoring the RXDK_STAGED_SDK override.</summary>
    public static string GetStagedSdkRoot() =>
        EnvOverride("RXDK_STAGED_SDK") ?? GetDefaultStagedSdkRoot();

    // ---- Staged docs (RXDK-Docs, …/RXDK/docs) ----

    public static string GetDefaultStagedDocsRoot() =>
        Path.Combine(RxdkDataRoot(), "docs");

    /// <summary>Effective staged docs root, honoring the RXDK_STAGED_DOCS override.</summary>
    public static string GetStagedDocsRoot() =>
        EnvOverride("RXDK_STAGED_DOCS") ?? GetDefaultStagedDocsRoot();

    // ---- Staged samples (RXDK-Samples, …/RXDK/samples) ----

    public static string GetDefaultStagedSamplesRoot() =>
        Path.Combine(RxdkDataRoot(), "samples");

    /// <summary>Effective staged samples root, honoring the RXDK_STAGED_SAMPLES override.</summary>
    public static string GetStagedSamplesRoot() =>
        EnvOverride("RXDK_STAGED_SAMPLES") ?? GetDefaultStagedSamplesRoot();

    // ---- Managed LLVM toolchain install ----
    // Windows: %LocalAppData%\RXDK\llvm (user-local; not ProgramData).
    // Linux/macOS: sibling of tools/sdk under the same RXDK data root.

    /// <summary>Managed install root for the RXDK LLVM toolchain (the xboxog clang/lld fork).
    /// An unpacked xboxog-&lt;os&gt;-&lt;arch&gt; lives under here.</summary>
    public static string GetLlvmInstallRoot() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(LocalAppData(), "RXDK", "llvm")
            : Path.Combine(RxdkDataRoot(), "llvm");

    private static string? EnvOverride(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value.Trim());
    }
}
