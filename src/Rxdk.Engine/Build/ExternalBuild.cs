using System.Text;
using Rxdk.Engine.Platform;

namespace Rxdk.Engine.Build;

/// <summary>
/// The steps a build system other than <c>rxdk build</c> (the CMake toolchain) runs through
/// Rxdk.Cli: compile through the TLS retry, and link a title from its objects and libraries.
/// The recipes are <see cref="XboxRecipe"/>'s, so these match what <c>rxdk build</c> does.
/// </summary>
public static class ExternalBuild
{
    /// <summary>The objects, the title's own libraries and the SDK library names of a link.</summary>
    public sealed record LinkInputs(List<string> Objects, List<string> UserLibraries, List<string> SdkLibraries);

    /// <summary>
    /// Sorts link inputs the way a CMake link line carries them: object files (.obj/.o), library
    /// files with a directory (a CMake target's archive, an imported library), and bare library
    /// names (libd3d8.lib), which are SDK libraries to find on the search path.
    /// </summary>
    public static LinkInputs ClassifyLinkInputs(IEnumerable<string> inputs)
    {
        var objs = new List<string>();
        var userLibs = new List<string>();
        var sdkLibs = new List<string>();
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input)) continue;
            var ext = Path.GetExtension(input).ToLowerInvariant();
            if (ext is ".obj" or ".o")
                objs.Add(input);
            else if (Path.IsPathRooted(input) || input.Contains('/') || input.Contains('\\'))
                userLibs.Add(input);
            else
                sdkLibs.Add(ext.Length == 0 ? input + ".lib" : input);
        }
        return new LinkInputs(objs, userLibs, sdkLibs);
    }

    /// <summary>
    /// Expands @file arguments (one or more arguments per line, double-quoted when they hold
    /// spaces), as CMake writes them for long object lists.
    /// </summary>
    public static List<string> ExpandResponseFiles(IEnumerable<string> args)
    {
        var result = new List<string>();
        foreach (var arg in args)
        {
            if (arg.Length > 1 && arg[0] == '@' && File.Exists(arg[1..]))
                result.AddRange(SplitCommandLine(File.ReadAllText(arg[1..])));
            else
                result.Add(arg);
        }
        return result;
    }

    /// <summary>Splits response-file text into arguments: whitespace-separated, "quotes" group, \" escapes.</summary>
    public static List<string> SplitCommandLine(string text)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        var inQuotes = false;
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '"')
            {
                cur.Append('"');
                i++;
                any = true;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                any = true;
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (any) args.Add(cur.ToString());
                cur.Clear();
                any = false;
            }
            else
            {
                cur.Append(c);
                any = true;
            }
        }
        if (any) args.Add(cur.ToString());
        return args;
    }

    /// <summary>
    /// Links a title: <see cref="XboxRecipe.PlanLink"/> orders the libraries and picks the entry
    /// point, and <see cref="XdkLink.LinkAsync"/> links, both exactly as <c>rxdk build</c> does.
    /// </summary>
    public static Task<ProcessResult> LinkAsync(
        Toolchain tc, LinkInputs inputs, IReadOnlyList<string> extraLibDirs, string outExe,
        bool debugInfo, bool isDxt, Action<string>? log, CancellationToken ct)
    {
        var searchDirs = new List<string> { SdkLayout.GetSdkLibDir() };
        searchDirs.AddRange(extraLibDirs);
        var plan = XboxRecipe.PlanLink(inputs.SdkLibraries, inputs.UserLibraries, searchDirs, isDxt);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outExe))!);
        return XdkLink.LinkAsync(tc, inputs.Objects, plan.LibraryArgs, outExe, plan.Entry, debugInfo, log, ct);
    }

    /// <summary>
    /// Runs a compile command and, as <c>rxdk build</c> does, recompiles with line tables only
    /// when a full-debug object turns out to use thread_local: emulated TLS leaves a debug record
    /// pointing at a symbol nothing defines, which would fail the link.
    /// </summary>
    public static async Task<int> CompileWithTlsRetryAsync(IReadOnlyList<string> command, Action<string>? log, CancellationToken ct)
    {
        if (command.Count == 0) throw new ArgumentException("compile-launch: no compiler command");
        var exe = command[0];
        var args = command.Skip(1).ToList();
        var result = await ProcessRunner.RunStreamedAsync(exe, args, log, ct: ct, echoCommand: false);
        if (!result.Success) return result.ExitCode;

        var obj = ObjectPath(args);
        if (obj is not null && NeedsTlsRetry(args) && XboxBuild.ObjUsesEmulatedTls(obj))
        {
            log?.Invoke($"{Path.GetFileName(obj)}: uses thread_local; rebuilding with line-tables-only " +
                        "debug info (locals unavailable in this file) to keep the emulated-TLS link clean.");
            var retry = new List<string>(args) { "-gline-tables-only" };
            result = await ProcessRunner.RunStreamedAsync(exe, retry, log, ct: ct, echoCommand: false);
        }
        return result.ExitCode;
    }

    /// <summary>The object a compile command writes (-o X or -oX), or null.</summary>
    public static string? ObjectPath(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Count) return args[i + 1];
            if (args[i].StartsWith("-o", StringComparison.Ordinal) && args[i].Length > 2) return args[i][2..];
        }
        return null;
    }

    /// <summary>True when the command asks for full debug info (-g) rather than line tables only.</summary>
    public static bool NeedsTlsRetry(IReadOnlyList<string> args)
    {
        var full = false;
        foreach (var a in args)
        {
            if (a == "-g") full = true;
            else if (a is "-gline-tables-only" or "-g0") full = false;
        }
        return full;
    }
}
