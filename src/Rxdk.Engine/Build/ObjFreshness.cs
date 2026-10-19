#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Rxdk.Engine.Build;

/// <summary>
/// The incremental-compile rule shared by the engine build (rxdk build, VS Code) and the MSBuild
/// compile task (VS20XX), which links this file in: an object is current only when recompiling it
/// would reproduce it. Plain C# so it builds for net8.0 and net472 alike.
/// </summary>
internal static class ObjFreshness
{
    /// <summary>
    /// The command that builds an object, one token per line: the compiler, then its arguments.
    /// Recorded next to the object (<c>&lt;obj&gt;.cmd</c>) and compared on the next build, so a
    /// changed define, include path, flag, configuration or compiler rebuilds it.
    /// </summary>
    public static string Signature(string compilerExe, IEnumerable<string> toolArgs) =>
        string.Join("\n", new[] { compilerExe }.Concat(toolArgs)) + "\n";

    /// <summary>The sidecar that records the command an object was built with.</summary>
    public static string CommandFile(string obj) => obj + ".cmd";

    /// <summary>
    /// True when <paramref name="obj"/> exists, was built by <paramref name="signature"/> (recorded
    /// in <c><paramref name="obj"/>.cmd</c>), and is at least as new as every source and every header
    /// recorded in the sidecar depfile (<c><paramref name="obj"/>.d</c>). A missing object, depfile or
    /// command record, a different command, a vanished input, or any input newer than the object
    /// returns false (recompile).
    /// </summary>
    public static bool IsUpToDate(IEnumerable<string> sources, string obj, string signature)
    {
        if (!File.Exists(obj)) return false;
        var cmdFile = CommandFile(obj);
        if (!File.Exists(cmdFile) || File.ReadAllText(cmdFile) != signature) return false;
        var objTime = File.GetLastWriteTimeUtc(obj);
        foreach (var src in sources)
            if (!File.Exists(src) || File.GetLastWriteTimeUtc(src) > objTime) return false;

        var depFile = obj + ".d";
        if (!File.Exists(depFile)) return false; // no recorded deps -> can't prove fresh
        foreach (var dep in ParseDepfile(depFile))
        {
            // A dependency that no longer exists means a header was moved/deleted: recompile so the
            // failure (or the newly-resolved header) is seen now, not silently skipped.
            if (!File.Exists(dep)) return false;
            if (File.GetLastWriteTimeUtc(dep) > objTime) return false;
        }
        return true;
    }

    /// <summary>
    /// Parse a clang/Make-style depfile into its prerequisite paths. The format is
    /// <c>target: dep1 dep2 \</c> with line continuations, spaces inside a path escaped as
    /// <c>\ </c>, and the target (everything up to the first unescaped <c>:</c>) dropped.
    /// </summary>
    public static IEnumerable<string> ParseDepfile(string depFile)
    {
        string text;
        try { text = File.ReadAllText(depFile); }
        catch { yield break; }

        // Join line continuations, then drop the "target:" prefix. The target is itself a Windows
        // path, so its drive-letter colon ("C:\...") must NOT be mistaken for the rule separator:
        // split on the first colon that is followed by whitespace (or end), which is the real
        // "target: deps" colon. clang emits a single rule, so the first such colon is enough.
        text = text.Replace("\\\r\n", " ").Replace("\\\n", " ").Replace("\r", " ").Replace("\n", " ");
        var colon = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ':' && (i + 1 >= text.Length || text[i + 1] == ' ' || text[i + 1] == '\t'))
            {
                colon = i;
                break;
            }
        }
        if (colon >= 0) text = text.Substring(colon + 1);

        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == ' ')
            {
                sb.Append(' '); // escaped space inside a path
                i++;
            }
            else if (c == ' ' || c == '\t')
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
