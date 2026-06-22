using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Hardware;

/// <summary>
/// Clean ("autoRun") TriangleXDK launch: SetTitle + Go with no debugger connect, no initial
/// breakpoint and no stopon. DmConnectDebugger(TRUE) hangs D3D CreateDevice after InitHardware,
/// so attaching the debugger before/while running gives a black screen (see RXDK-VSCode
/// session.c CmdLaunch autoRun path). "Running" is signalled off the title MODLOAD notification.
/// </summary>
internal sealed class TriangleLaunchHelper : IDisposable
{
    private readonly IXbdmDebugConnection _debug;
    private readonly string _titleBase;
    private readonly ManualResetEventSlim _breakEvent = new(false);
    private IXbdmNotificationSession? _notify;
    private bool _awaiting;

    internal TriangleLaunchHelper(IXbdmDebugConnection debug, string titleFileName)
    {
        _debug = debug;
        _titleBase = Path.GetFileNameWithoutExtension(titleFileName).ToLowerInvariant();
    }

    internal bool LaunchAndWaitForRunning(
        string wireDir,
        string title,
        TimeSpan timeout,
        out XbdmModLoadNotification? module)
    {
        module = null;
        OpenLaunchNotifications();

        _awaiting = true;
        _breakEvent.Reset();

        // Clean launch: no cmdline, no debugger connect, no breakpoint, no stopon.
        _debug.SetTitle(wireDir, title, null);
        _debug.Go();

        var running = XbdmKitWait.Until(
            () => _breakEvent.IsSet,
            timeout,
            progressLabel: "Waiting for TriangleXDK to start");

        _awaiting = false;
        if (!running)
            return false;

        module = _debug.WalkLoadedModules().FirstOrDefault(XbdmKitWait.IsTriangleModule);
        return module is not null;
    }

    private void OpenLaunchNotifications()
    {
        _notify?.Dispose();
        _debug.UseSharedConnection(true);
        _debug.UseSharedConnection(false);
        _debug.UseSharedConnection(true);

        _notify = _debug.OpenNotificationSession(XbdmDebugConstants.DmPersistent);
        _notify.Notify(XbdmDebugConstants.DmModLoad, OnNotify);
    }

    private void OnNotify(uint notification, object? data)
    {
        if (!_awaiting)
            return;

        var code = notification & XbdmDebugConstants.NotificationMask;

        // Title module loaded => the title is up and rendering. Don't stop it.
        if (code == XbdmDebugConstants.DmModLoad &&
            data is XbdmModLoadNotification mod &&
            ModuleMatches(mod.Name))
        {
            _breakEvent.Set();
        }
    }

    private bool ModuleMatches(string name)
    {
        var baseName = Path.GetFileName(name.Replace('/', '\\')).ToLowerInvariant();
        return baseName.Contains(_titleBase, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _notify?.Dispose();
        _breakEvent.Dispose();
    }
}
