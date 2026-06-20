using System.Globalization;
using System.Text;

namespace Rxdk.XbShellExt.Diagnostics;

// Lightweight tracer for the managed shell extension. Writes to a dedicated
// file (NOT the native proxy's log) because .NET's FileMode.Append is not an
// atomic OS append; interleaving with the native proxy's heavy FILE_APPEND_DATA
// writes silently clobbers managed lines. Correlate with the native log by the
// timestamp prefix (same HH:mm:ss.fff clock).
internal static class ManagedTrace
{
    private const string LogPath = @"C:\Temp\xb-shlext-mgd.log";
    private static readonly object Gate = new();

    public static void Line(string message)
    {
        try
        {
            var now = DateTime.Now;
            var tid = Environment.CurrentManagedThreadId;
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{now:HH:mm:ss.fff} tid={tid} [mgd] {message}\r\n");

            lock (Gate)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using var stream = new FileStream(
                    LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
            // Tracing must never throw into shell call paths.
        }
    }
}
