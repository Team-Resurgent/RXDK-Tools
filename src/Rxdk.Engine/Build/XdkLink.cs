using System.Linq;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Build;

/// <summary>
/// Links Xbox title objects with Zig, mirroring the SDK's own title link (build/link_pe.zig).
/// C# port of RXDK-VSCode xdkLink.ts. A title is: its objects + SDK libs, linked
/// -nostdlib -nostartfiles at the XBE image base, with compiler-rt and an explicit entry.
/// libcompat[d].lib is like any other "Additional Dependencies" entry -- the project must name
/// it explicitly (it is NOT auto-injected) -- except it is force-linked whole-archive, since a
/// normal link only pulls in symbols something else already references, and the whole point of
/// libcompat is to win a COMDAT tie-break against zig's compiler-rt even when nothing in the
/// title calls its functions directly (see xdkLink.ts for the full hardware rationale).
/// </summary>
public static class XdkLink
{
    /// <summary>True for a resolved lib path whose base name (case-insensitive, minus ".lib") is
    /// "libcompat" or "libcompatd" -- the one dependency that needs --whole-archive, not a normal
    /// link. Matched by name, not a fixed path, since the caller resolves it like any other
    /// "Additional Dependencies" entry.</summary>
    public static bool IsWholeArchiveLib(string resolvedPath)
    {
        var stem = Path.GetFileNameWithoutExtension(resolvedPath);
        return stem.Equals("libcompat", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("libcompatd", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<ProcessResult> LinkAsync(
        Toolchain tc,
        IReadOnlyList<string> objs,
        IReadOnlyList<string> libs,
        string outExe,
        string entry = "start",
        bool debugInfo = true,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var args = new List<string>(tc.LinkSubcommand());

        // C++ exception unwinding: libcpp.lib bundles libunwind, whose baremetal frame lookup
        // reads &__eh_frame_start / &__eh_frame_end to find the merged .eh_frame. Those bounds
        // aren't linker-provided on this PE target, so we bracket the section with two tiny CRT
        // marker objects — begin linked first, end linked last — exactly like the SDK's own title
        // link (build/link_pe.zig). Only needed when libcpp is in the link (STL / exceptions).
        var linksLibcpp = libs.Any(l => Path.GetFileName(l).Contains("libcpp", StringComparison.OrdinalIgnoreCase));
        string? ehBegin = null, ehEnd = null;
        if (linksLibcpp)
            (ehBegin, ehEnd) = await CompileEhBracketsAsync(tc, Path.GetDirectoryName(Path.GetFullPath(outExe))!, log, ct);
        if (ehBegin is not null) args.Add(ehBegin);

        // Objects go through a response file: a full title has hundreds of them, and passing every
        // path on the command line blows past the Windows CreateProcess limit (~32 KB) -> the link
        // fails with "The filename or extension is too long". Clang/zig read @file, one arg per line.
        var rsp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outExe))!, "link_objs.rsp");
        await File.WriteAllLinesAsync(rsp, objs.Select(o => "\"" + o.Replace('\\', '/') + "\""), ct);
        args.Add("@" + rsp);

        args.AddRange(libs);
        // compiler-rt builtins (LLVM only): satisfies the SDK libs' 64-bit integer helpers
        // (__divdi3/__udivdi3/…). Placed AFTER the libs so lld resolves their undefined refs
        // against it; as a plain archive it is pulled on demand, leaving libcompat's
        // whole-archive fabs/memmove overrides intact. zig has no archive here (its driver
        // auto-links compiler-rt), so this is a no-op for the zig path.
        if (tc.BuiltinsArchive is not null) args.Add(tc.BuiltinsArchive);
        if (ehEnd is not null) args.Add(ehEnd); // ___eh_frame_end must follow every .eh_frame contributor
        args.AddRange(new[]
        {
            "-target", tc.TargetTriple,
            // Must match XboxBuild.cs's compile recipe. The Xbox is a Coppermine Pentium III --
            // CPUID reports SSE but not SSE2 -- so pin the CPU or codegen (and, under zig, the
            // compiler-rt selection below) picks an x86 baseline that uses SSE2 for double and
            // 64-bit integer math, whose encodings are invalid opcodes on the console.
            "-march=pentium3",
            "-nostdlib", "-nostartfiles",
            "-Wl,--image-base=0x10000",
            "-O0",
        });
        if (debugInfo) args.Add("-g");
        // Runtime lib: zig pulls its bundled compiler-rt; the LLVM fork ships none, so it links
        // -fuse-ld=lld and takes any builtins from the explicit SDK libs (see Toolchain).
        args.AddRange(tc.LinkRuntimeArgs());
        args.AddRange(new[] { "-e", string.IsNullOrEmpty(entry) ? "start" : entry, "-o", outExe });

        return await ProcessRunner.RunStreamedAsync(tc.CompilerExe, args, log, ct: ct);
    }

    // The two .eh_frame bracket markers (i386 COFF mangles C __eh_frame_start -> ___eh_frame_start).
    // Kept in-engine (rather than shipped in the SDK) because they're a pure link-time concern.
    private const string EhBeginAsm =
        ".section .eh_frame,\"dr\"\n.globl ___eh_frame_start\n___eh_frame_start:\n";
    private const string EhEndAsm =
        ".section .eh_frame,\"dr\"\n.globl ___eh_frame_end\n___eh_frame_end:\n";

    /// <summary>
    /// Writes and compiles the two .eh_frame bracket markers next to the output. Returns
    /// (beginObj, endObj) to place first/last in the link, or (null, null) if compilation fails
    /// (the link then surfaces the missing-symbol error, which is the actionable diagnostic).
    /// </summary>
    private static async Task<(string?, string?)> CompileEhBracketsAsync(
        Toolchain tc, string outDir, Action<string>? log, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(outDir);
            async Task<string?> CompileAsync(string stem, string asm)
            {
                var src = Path.Combine(outDir, stem + ".S");
                var obj = Path.Combine(outDir, stem + ".o");
                await File.WriteAllTextAsync(src, asm, ct);
                var asmArgs = new List<string>(tc.CompileSubcommand(false));
                asmArgs.AddRange(new[] { "-target", tc.TargetTriple, "-march=pentium3", "-c", src, "-o", obj });
                var r = await ProcessRunner.RunStreamedAsync(tc.CompilerExe, asmArgs, log, ct: ct);
                return r.Success ? obj : null;
            }
            var begin = await CompileAsync("rxdk_eh_begin", EhBeginAsm);
            var end = await CompileAsync("rxdk_eh_end", EhEndAsm);
            if (begin is null || end is null)
            {
                log?.Invoke("Warning: could not build .eh_frame brackets; C++ exception unwinding may fail to link.");
                return (null, null);
            }
            return (begin, end);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: .eh_frame bracket setup failed ({ex.Message}); continuing without it.");
            return (null, null);
        }
    }
}
