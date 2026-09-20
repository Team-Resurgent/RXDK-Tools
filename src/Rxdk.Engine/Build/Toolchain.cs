using Rxdk.Engine.Bootstrap;

namespace Rxdk.Engine.Build;

/// <summary>
/// The compiler/archiver/linker used to build a title. Two backends during the zig->LLVM
/// migration: the vendored <c>zig</c> (compile via <c>zig cc/c++</c>, archive via <c>zig ar</c>,
/// link via the <c>zig cc</c> driver with its bundled compiler-rt) and the RXDK <c>clang</c> fork
/// (compile/link with <c>clang</c> directly, archive with <c>llvm-ar</c>, link with lld). The LLVM
/// path is opt-in: selected when the RXDK LLVM toolchain resolves (RXDK_LLVM env / managed
/// install), otherwise zig. The two differ only in the executable, the target triple, a
/// subcommand token that zig needs and clang does not, the resource-include the -nostdinc clang
/// build must re-add, and the link driver's runtime-lib selection.
/// </summary>
public sealed class Toolchain
{
    public bool IsLlvm { get; private init; }

    /// <summary>Compiler + link driver executable (zig.exe or clang.exe).</summary>
    public string CompilerExe { get; private init; } = "";

    /// <summary>Static-archive tool (zig.exe or llvm-ar.exe); same "rcs archive @rsp" syntax.</summary>
    public string ArchiverExe { get; private init; } = "";

    /// <summary>Clang target triple: zig's <c>x86-windows-gnu</c> vs the fork's canonical
    /// <c>i686-pc-windows-gnu</c>. Both are 32-bit x86 PE/COFF via lld.</summary>
    public string TargetTriple { get; private init; } = "x86-windows-gnu";

    /// <summary>compiler-rt builtins archive to append to the link, or null. Empty for zig (its
    /// driver auto-links compiler-rt); for LLVM this is <c>libclang_rt.builtins-i386.a</c>, which the
    /// -nostdlib clang link must add explicitly to satisfy the SDK libs' 64-bit integer builtins.</summary>
    public string? BuiltinsArchive { get; private init; }

    /// <summary>Extra include args every compile needs. Empty for zig (zig cc keeps clang's
    /// resource dir on the path); for LLVM the -nostdinc build must re-add
    /// <c>-isystem &lt;root&gt;/lib/clang/&lt;ver&gt;/include</c> or stddef.h/stdarg.h are unfound.</summary>
    public IReadOnlyList<string> ResourceIncludeArgs { get; private init; } = Array.Empty<string>();

    /// <summary>Human label for diagnostics.</summary>
    public string Name => IsLlvm ? "LLVM (clang)" : "Zig";

    /// <summary>Leading subcommand token for a compile invocation. zig needs <c>cc</c>/<c>c++</c>;
    /// clang is the compiler itself (the -x flag states the language) so it needs none.</summary>
    public IEnumerable<string> CompileSubcommand(bool isCpp) =>
        IsLlvm ? Array.Empty<string>() : new[] { isCpp ? "c++" : "cc" };

    /// <summary>Leading token for an archive invocation. zig: <c>ar</c>; llvm-ar: none.</summary>
    public IEnumerable<string> ArchiveSubcommand() =>
        IsLlvm ? Array.Empty<string>() : new[] { "ar" };

    /// <summary>Leading token for a link (driver) invocation. zig: <c>cc</c>; clang: none.</summary>
    public IEnumerable<string> LinkSubcommand() =>
        IsLlvm ? Array.Empty<string>() : new[] { "cc" };

    /// <summary>Link-driver runtime-lib args. zig selects its bundled compiler-rt; the fork clang
    /// ships no compiler-rt, so pin lld and let the runtime come from the explicit SDK libs
    /// (libc/libc++/libcompat) -- the RXDK link is -nostdlib, and title codegen for i686 rarely
    /// needs a compiler-rt builtin (they inline). If one is ever undefined, add it to libcompat.</summary>
    public IEnumerable<string> LinkRuntimeArgs() =>
        IsLlvm ? new[] { "-fuse-ld=lld" } : new[] { "-rtlib=compiler-rt" };

    private static Toolchain Zig(string zig) => new()
    {
        IsLlvm = false,
        CompilerExe = zig,
        ArchiverExe = zig,
        TargetTriple = "x86-windows-gnu",
        ResourceIncludeArgs = Array.Empty<string>(),
    };

    private static Toolchain Llvm(string root)
    {
        var resource = LlvmRuntime.ResourceInclude(root);
        var inc = resource is not null ? new[] { "-isystem", resource } : Array.Empty<string>();
        return new Toolchain
        {
            IsLlvm = true,
            CompilerExe = LlvmRuntime.ClangExe(root),
            ArchiverExe = LlvmRuntime.ArExe(root),
            TargetTriple = "i686-pc-windows-gnu",
            ResourceIncludeArgs = inc,
            BuiltinsArchive = LlvmRuntime.BuiltinsArchive(root),
        };
    }

    /// <summary>
    /// Resolve the toolchain for a build. LLVM is now the <b>default</b> when it resolves: the RXDK
    /// clang/lld fork is used whenever it is installed (explicit <paramref name="llvmOverride"/> /
    /// <c>RXDK_LLVM</c> path / managed install), with zig as the fallback and the opt-out. Precedence:
    /// <list type="number">
    /// <item>Explicit <paramref name="llvmOverride"/> path → LLVM (a caller/flag forcing it).</item>
    /// <item>Zig opt-out — <c>RXDK_USE_ZIG</c> truthy, or <c>RXDK_USE_LLVM</c> falsey (0/false/no/off)
    ///   — → zig (throws if zig isn't available).</item>
    /// <item>Otherwise prefer LLVM when it resolves; if <c>RXDK_USE_LLVM</c> is truthy it is
    ///   <i>required</i> (throws when absent rather than silently using zig).</item>
    /// <item>Else zig (explicit <paramref name="zigOverride"/> / RXDK_ZIG / managed install / PATH).</item>
    /// </list>
    /// SELECTION is still separate from LOCATION — <see cref="LlvmRuntime.ResolveRoot"/> only says
    /// where LLVM is; this method decides whether to use it. Flipping the default here (rather than in
    /// the callers) is the single switch that ended the opt-in phase.
    /// </summary>
    public static async Task<Toolchain> ResolveAsync(
        string? zigOverride = null, string? llvmOverride = null, CancellationToken ct = default)
    {
        // 1. Programmatic override always wins.
        if (!string.IsNullOrWhiteSpace(llvmOverride))
        {
            var root = LlvmRuntime.ResolveRoot(llvmOverride)
                ?? throw new InvalidOperationException($"LLVM toolchain not found at override: {llvmOverride}");
            return Llvm(root);
        }

        var useLlvm = Environment.GetEnvironmentVariable("RXDK_USE_LLVM")?.Trim();
        var forceZig = IsTruthy(Environment.GetEnvironmentVariable("RXDK_USE_ZIG")) || IsFalsey(useLlvm);
        var requireLlvm = IsTruthy(useLlvm);

        // 2. Explicit zig opt-out.
        if (forceZig && !requireLlvm)
        {
            var zigOut = await ZigRuntime.ResolveZigExecutableAsync(zigOverride, ct)
                ?? throw new InvalidOperationException(
                    "Zig was requested (RXDK_USE_ZIG / RXDK_USE_LLVM=0) but not found. Install Zig " +
                    "(install-zig) or add zig to PATH, or unset the override to use LLVM.");
            return Zig(zigOut);
        }

        // 3. Default: prefer LLVM when it resolves (RXDK_LLVM path / managed install).
        var llvmRoot = LlvmRuntime.ResolveRoot();
        if (llvmRoot is not null)
            return Llvm(llvmRoot);
        if (requireLlvm)
            throw new InvalidOperationException(
                "LLVM was required (RXDK_USE_LLVM=1) but no toolchain was found. Run install-llvm, " +
                "or point RXDK_LLVM at an unpacked xboxog-<os>-<arch> root.");

        // 4. Zig fallback.
        var zig = await ZigRuntime.ResolveZigExecutableAsync(zigOverride, ct)
                  ?? throw new InvalidOperationException(
                      "No toolchain found. Run install-llvm (recommended) or install-zig, or add " +
                      "zig to PATH.");
        return Zig(zig);
    }

    private static bool IsTruthy(string? v)
    {
        v = v?.Trim();
        return v is not null && (v is "1"
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFalsey(string? v)
    {
        v = v?.Trim();
        return v is not null && (v is "0"
            || v.Equals("false", StringComparison.OrdinalIgnoreCase)
            || v.Equals("no", StringComparison.OrdinalIgnoreCase)
            || v.Equals("off", StringComparison.OrdinalIgnoreCase));
    }
}
