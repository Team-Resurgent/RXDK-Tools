using System.Globalization;
using System.Text;

namespace Rxdk.XbShellExt.Diagnostics;

// Lightweight tracer for the managed shell extension. Enabled by default; set
// XB_SHLEXT_TRACE=0 to disable. Logs go to ProgramData (see ShellTracePaths).
internal static class ManagedTrace
{
    private static readonly object Gate = new();
    private static readonly bool Enabled = ResolveEnabled();
    private static string? _logPath;

    public static void Line(string message)
    {
        if (!Enabled)
            return;

        try
        {
            var now = DateTime.Now;
            var tid = Environment.CurrentManagedThreadId;
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{now:HH:mm:ss.fff} tid={tid} [mgd] {message}\r\n");

            lock (Gate)
            {
                var logPath = ResolveLogPath();
                Directory.CreateDirectory(ShellTracePaths.LogDirectory);

                using var stream = new FileStream(
                    logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
            // Tracing must never throw into shell call paths.
        }
    }

    private static string ResolveLogPath() =>
        _logPath ??= ShellTracePaths.ManagedLogPath;

    private static bool ResolveEnabled()
    {
        var env = Environment.GetEnvironmentVariable("XB_SHLEXT_TRACE");
        if (string.Equals(env, "0", StringComparison.Ordinal) ||
            string.Equals(env, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(env, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
