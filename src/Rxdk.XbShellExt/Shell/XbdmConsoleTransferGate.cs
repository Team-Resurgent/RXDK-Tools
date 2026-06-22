using System.Collections.Concurrent;

namespace Rxdk.XbShellExt.Shell;

/// <summary>
/// Serializes drag/paste XBDM downloads per console. Independent from browse so Explorer
/// can still open drives while a desktop copy is running.
/// </summary>
internal static class XbdmConsoleTransferGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    [ThreadStatic]
    private static string? t_heldConsole;

    [ThreadStatic]
    private static int t_holdDepth;

    internal readonly struct Scope : IDisposable
    {
        private readonly string _consoleName;

        internal Scope(string consoleName)
        {
            _consoleName = consoleName;
            if (t_holdDepth > 0 &&
                string.Equals(t_heldConsole, consoleName, StringComparison.OrdinalIgnoreCase))
            {
                t_holdDepth++;
                return;
            }

            Gates.GetOrAdd(consoleName, static _ => new SemaphoreSlim(1, 1)).Wait();
            t_heldConsole = consoleName;
            t_holdDepth = 1;
        }

        public void Dispose()
        {
            if (t_holdDepth <= 0)
                return;

            t_holdDepth--;
            if (t_holdDepth != 0)
                return;

            Gates.GetOrAdd(_consoleName, static _ => new SemaphoreSlim(1, 1)).Release();
            t_heldConsole = null;
        }
    }

    internal static Scope Enter(string consoleName) => new(consoleName);
}
