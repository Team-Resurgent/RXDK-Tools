using System.Runtime.InteropServices;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Bootstrap;

/// <summary>
/// Locates the RXDK LLVM toolchain (the Team-Resurgent clang/lld/llvm-lib fork built for the
/// xboxog target) used to compile + link titles when the LLVM path is selected. The SDK
/// libraries are built and tested with exactly this clang, so codegen/predefined macros match.
///
/// Resolution order (opt-in during the zig->LLVM migration): explicit override -> RXDK_LLVM env
/// (the ROOT of an unpacked xboxog-&lt;os&gt;-&lt;arch&gt;.zip: the dir holding bin/clang + lib/clang/
/// &lt;ver&gt;/include) -> managed pinned install. Returns null when none is available, which the
/// caller treats as "LLVM not selected/installed" and falls back to zig.
/// </summary>
public static class LlvmRuntime
{
    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>The unpacked toolchain root for this host, under the managed install dir.</summary>
    private static string ArchiveDirName
    {
        get
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            if (OperatingSystem.IsWindows()) return $"xboxog-windows-{arch}";
            if (OperatingSystem.IsMacOS()) return $"xboxog-macos-{arch}";
            return $"xboxog-linux-{arch}";
        }
    }

    private static IEnumerable<string> InstalledRootCandidates()
    {
        var root = RxdkPaths.GetLlvmInstallRoot();
        yield return Path.Combine(root, ArchiveDirName);
        yield return root; // in case the archive was unpacked flat
    }

    /// <summary>
    /// Resolve the LLVM toolchain root (the dir containing bin/ and lib/clang/&lt;ver&gt;/include),
    /// or null when the LLVM path is not selected/available. A root is valid only when bin/clang
    /// exists under it.
    /// </summary>
    public static string? ResolveRoot(string? overrideRoot = null)
    {
        static bool IsRoot(string dir) =>
            !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "bin", Exe("clang")));

        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            var full = Path.GetFullPath(overrideRoot);
            return IsRoot(full) ? full : throw new DirectoryNotFoundException(
                $"RXDK LLVM root has no bin/clang: {full}");
        }

        var env = Environment.GetEnvironmentVariable("RXDK_LLVM");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var full = Path.GetFullPath(env.Trim());
            return IsRoot(full) ? full : throw new DirectoryNotFoundException(
                $"RXDK_LLVM has no bin/clang: {full}");
        }

        foreach (var c in InstalledRootCandidates())
            if (IsRoot(c)) return c;

        return null;
    }

    public static string ClangExe(string root) => Path.Combine(root, "bin", Exe("clang"));

    /// <summary>The C++ driver (clang++). Falls back to clang if the ++ alias is absent (clang in
    /// C++ mode compiles .cpp identically given an explicit target/-x).</summary>
    public static string ClangxxExe(string root)
    {
        var cxx = Path.Combine(root, "bin", Exe("clang++"));
        return File.Exists(cxx) ? cxx : ClangExe(root);
    }

    /// <summary>The MSVC-style COFF librarian (llvm-lib), used to pack the SDK libs with the same
    /// <c>/NOLOGO /OUT: @rsp</c> switches the current zig build uses (see build/coff_lib.zig).</summary>
    public static string LibExe(string root) => Path.Combine(root, "bin", Exe("llvm-lib"));

    /// <summary>The archiver. llvm-ar takes the same "rcs archive @rsp" syntax as `zig ar`, so the
    /// engine's archive step swaps only the executable. Falls back to llvm-lib if llvm-ar is absent.</summary>
    public static string ArExe(string root)
    {
        var ar = Path.Combine(root, "bin", Exe("llvm-ar"));
        return File.Exists(ar) ? ar : Path.Combine(root, "bin", Exe("llvm-lib"));
    }

    /// <summary>
    /// The single versioned clang builtin-include dir (&lt;root&gt;/lib/clang/&lt;major&gt;/include),
    /// which the title compile must re-add via -isystem: it builds with -nostdinc (unlike zig cc,
    /// which keeps clang's resource dir on the search path), so stddef.h/stdarg.h/... would
    /// otherwise be unfound. Returns null if the layout is unexpected.
    /// </summary>
    public static string? ResourceInclude(string root)
    {
        var clangLib = Path.Combine(root, "lib", "clang");
        if (!Directory.Exists(clangLib)) return null;
        foreach (var ver in Directory.EnumerateDirectories(clangLib))
        {
            var inc = Path.Combine(ver, "include");
            if (Directory.Exists(inc)) return inc;
        }
        return null;
    }

    /// <summary>
    /// The compiler-rt builtins archive for the i386 target (<c>libclang_rt.builtins-i386.a</c>),
    /// or null when the toolchain doesn't ship one. Unlike <c>zig cc</c> (which auto-links its
    /// bundled compiler-rt), a bare clang link is <c>-nostdlib</c> and provides no builtins, so the
    /// title link must add this archive explicitly or the SDK libs' 64-bit integer helpers
    /// (__divdi3/__udivdi3/__moddi3/__umoddi3, referenced by libcompat's MSVC __alldiv shim and
    /// picolibc's __ultoa_invert) are undefined. Searched at the canonical clang runtime path
    /// (&lt;root&gt;/lib/clang/&lt;ver&gt;/lib/windows) and a couple of common fallbacks. It is
    /// linked as an ordinary archive (pull-on-demand), so libcompat's whole-archive fabs/memmove
    /// still win the COMDAT tie-break.
    /// </summary>
    public static string? BuiltinsArchive(string root)
    {
        static string? FirstExisting(IEnumerable<string> dirs)
        {
            foreach (var d in dirs)
                foreach (var name in new[] { "libclang_rt.builtins-i386.a", "clang_rt.builtins-i386.lib" })
                {
                    var p = Path.Combine(d, name);
                    if (File.Exists(p)) return p;
                }
            return null;
        }
        var dirs = new List<string>();
        var clangLib = Path.Combine(root, "lib", "clang");
        if (Directory.Exists(clangLib))
            foreach (var ver in Directory.EnumerateDirectories(clangLib))
            {
                dirs.Add(Path.Combine(ver, "lib", "windows"));
                dirs.Add(Path.Combine(ver, "lib"));
            }
        dirs.Add(Path.Combine(root, "lib", "windows"));
        dirs.Add(Path.Combine(root, "lib"));
        return FirstExisting(dirs);
    }

    public static bool IsAvailable(string? overrideRoot = null)
    {
        try { return ResolveRoot(overrideRoot) is not null; }
        catch { return false; }
    }
}
