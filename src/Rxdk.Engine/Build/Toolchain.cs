using Rxdk.Engine.Bootstrap;

namespace Rxdk.Engine.Build;

/// <summary>
/// The compiler/archiver/linker used to build a title: the RXDK <c>clang</c>/<c>lld</c>/<c>llvm-ar</c>
/// fork (target <c>i686-pc-windows-gnu</c>). This was a zig-vs-LLVM abstraction during the migration;
/// zig has since been retired, so it now resolves the LLVM toolchain only. The subcommand/runtime
/// accessors are kept (returning the clang-native forms) so the link/compile callers are unchanged.
/// </summary>
public sealed class Toolchain
{
    /// <summary>Compiler + link driver executable (clang.exe).</summary>
    public string CompilerExe { get; private init; } = "";

    /// <summary>Static-archive tool (llvm-ar.exe / llvm-lib.exe).</summary>
    public string ArchiverExe { get; private init; } = "";

    /// <summary>The clang target triple (<c>i686-pc-windows-gnu</c>): 32-bit x86 PE/COFF via lld.</summary>
    public string TargetTriple { get; private init; } = "i686-pc-windows-gnu";

    /// <summary>The compiler-rt builtins archive (<c>libclang_rt.builtins-i386.a</c>) to append to the
    /// link, or null. The -nostdlib clang link ships no compiler-rt, so this supplies the SDK libs'
    /// 64-bit integer + stack-probe builtins.</summary>
    public string? BuiltinsArchive { get; private init; }

    /// <summary>Extra include args every compile needs: the clang resource dir, re-added via
    /// <c>-isystem &lt;root&gt;/lib/clang/&lt;ver&gt;/include</c> because the -nostdinc build otherwise
    /// can't find stddef.h/stdarg.h/….</summary>
    public IReadOnlyList<string> ResourceIncludeArgs { get; private init; } = Array.Empty<string>();

    /// <summary>Human label for diagnostics.</summary>
    public string Name => "LLVM (clang)";

    /// <summary>Leading subcommand token for a compile invocation — none: clang is the compiler
    /// itself (the -x flag states the language). Kept for the caller's signature.</summary>
    public IEnumerable<string> CompileSubcommand(bool isCpp) => Array.Empty<string>();

    /// <summary>Leading token for an archive invocation — none for llvm-ar.</summary>
    public IEnumerable<string> ArchiveSubcommand() => Array.Empty<string>();

    /// <summary>Leading token for a link (driver) invocation — none for clang.</summary>
    public IEnumerable<string> LinkSubcommand() => Array.Empty<string>();

    /// <summary>Link-driver runtime-lib args: pin lld. The fork clang ships no compiler-rt, so the
    /// runtime comes from the explicit SDK libs (and <see cref="BuiltinsArchive"/>).</summary>
    public IEnumerable<string> LinkRuntimeArgs() => new[] { "-fuse-ld=lld" };

    private static Toolchain Llvm(string root)
    {
        var resource = LlvmRuntime.ResourceInclude(root);
        var inc = resource is not null ? new[] { "-isystem", resource } : Array.Empty<string>();
        return new Toolchain
        {
            CompilerExe = LlvmRuntime.ClangExe(root),
            ArchiverExe = LlvmRuntime.ArExe(root),
            TargetTriple = "i686-pc-windows-gnu",
            ResourceIncludeArgs = inc,
            BuiltinsArchive = LlvmRuntime.BuiltinsArchive(root),
        };
    }

    /// <summary>
    /// Resolve the RXDK LLVM toolchain for a build: an explicit <paramref name="llvmOverride"/> path,
    /// else <c>RXDK_LLVM</c> / the managed install (<see cref="LlvmRuntime.ResolveRoot"/>). Throws with
    /// an actionable message when none is found.
    /// </summary>
    public static Task<Toolchain> ResolveAsync(string? llvmOverride = null, CancellationToken ct = default)
    {
        var root = LlvmRuntime.ResolveRoot(llvmOverride)
            ?? throw new InvalidOperationException(
                "No RXDK LLVM toolchain found. Run install-llvm, or set RXDK_LLVM to an unpacked " +
                "xboxog-<os>-<arch> clang root.");
        return Task.FromResult(Llvm(root));
    }
}
