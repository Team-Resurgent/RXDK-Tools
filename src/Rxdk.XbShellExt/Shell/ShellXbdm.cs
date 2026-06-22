using System.Collections.Concurrent;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbShellExt.Shell;

/// <summary>
/// Serializes shell folder browsing on the per-console SCI shared connection.
/// </summary>
internal static class ShellXbdm
{
    private static readonly ConcurrentDictionary<string, object> CommandLocks =
        new(StringComparer.OrdinalIgnoreCase);

    internal static T WithBrowse<T>(string consoleName, Func<XbdmSharedBrowseContext, T> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consoleName);

        var gate = CommandLocks.GetOrAdd(consoleName, static _ => new object());
        lock (gate)
        {
            try
            {
                return XbdmSharedBrowse.Run(consoleName, action);
            }
            catch (XbdmException ex) when (IsConnectionFailure(ex))
            {
                XbdmSharedBrowse.InvalidateConsole(consoleName);
                throw;
            }
            catch (IOException)
            {
                XbdmSharedBrowse.InvalidateConsole(consoleName);
                throw;
            }
        }
    }

    internal static void InvalidateConsole(string consoleName)
    {
        if (string.IsNullOrWhiteSpace(consoleName))
            return;

        XbdmSharedBrowse.InvalidateConsole(consoleName);
        CommandLocks.TryRemove(consoleName, out _);
    }

    private static bool IsConnectionFailure(XbdmException ex) =>
        ex.HResultCode is XbdmHResults.ConnectionLost or XbdmHResults.CannotConnect;
}
