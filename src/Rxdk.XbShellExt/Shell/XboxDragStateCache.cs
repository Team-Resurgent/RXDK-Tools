namespace Rxdk.XbShellExt.Shell;
/// <summary>
/// Holds the active drag catalog outside a single <see cref="Com.XboxFolder"/> instance.
/// Explorer may create multiple CC45 objects for one drag; the native data object keeps
/// its own managed UI pointer, but a second folder instance can still receive stream calls.
/// </summary>
internal static class XboxDragStateCache
{
    private static readonly object Gate = new();
    private static DragSnapshot? _active;

    internal sealed class DragSnapshot
    {
        public required string FolderPath { get; init; }
        public required IReadOnlyList<string> SelectionNames { get; init; }
        public required IReadOnlyList<XboxDragEntry> Catalog { get; init; }
        public required string ConsoleName { get; init; }
        public XboxDragTransferSession? Session { get; set; }
    }

    internal static void Publish(
        string folderPath,
        IReadOnlyList<nint> pidls,
        IReadOnlyList<XboxDragEntry> catalog,
        string consoleName)
    {
        var names = pidls.Select(PidlHelper.GetLastSegment).ToList();
        lock (Gate)
        {
            _active?.Session?.NotifyOwnerReleased();
            _active = new DragSnapshot
            {
                FolderPath = folderPath,
                SelectionNames = names,
                Catalog = catalog,
                ConsoleName = consoleName,
            };
        }
    }

    internal static bool TryRestore(string folderPath, out DragSnapshot? snapshot)
    {
        lock (Gate)
        {
            if (_active != null &&
                string.Equals(_active.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                snapshot = _active;
                return true;
            }
        }

        snapshot = null;
        return false;
    }

    internal static void AttachSession(XboxDragTransferSession session)
    {
        lock (Gate)
        {
            if (_active != null)
                _active.Session = session;
        }
    }

    internal static void ClearSession(XboxDragTransferSession session)
    {
        lock (Gate)
        {
            if (_active?.Session == session)
                _active.Session = null;
        }
    }
}
