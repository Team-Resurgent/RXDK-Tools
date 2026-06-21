using Rxdk.XbWatson.Core.BreakInfo;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbWatson.Core;

public sealed class WatsonSession : IDisposable
{
    private const int MaxNotificationStringLength = 1024;

    private readonly IWatsonEventSink _sink;
    private readonly object _gate = new();
    private XbdmConnection? _connection;
    private IXbdmDebugConnection? _debug;
    private IXbdmNotificationSession? _notifySession;
    private string _consoleName = "";
    private bool _connectedLogged;

    public WatsonSession(IWatsonEventSink sink) => _sink = sink;

    public string ConsoleName => _consoleName;

    public string? StartError { get; private set; }

    public bool TimestampsEnabled { get; set; }

    public bool KernelDebugOutputEnabled { get; private set; } = true;

    public bool KernelDebugAvailable { get; private set; }

    public bool KernelDebugProbed { get; private set; }

    public bool TryStart(string consoleName)
    {
        StartError = null;
        _consoleName = consoleName.Trim();
        if (string.IsNullOrWhiteSpace(_consoleName))
        {
            StartError = "Console name is required.";
            return false;
        }

        try
        {
            XbdmSession.EnsureInitialized();
            _connection = XbdmSession.Connect(_consoleName);
            _debug = _connection.Debug;
            _debug.UseSharedConnection(true);

            var flags = XbdmDebugConstants.DmDebugSession | XbdmDebugConstants.DmAsyncSession;
            _notifySession = _debug.OpenNotificationSession(flags);
            RegisterNotifications(_notifySession);
            return true;
        }
        catch (Exception ex)
        {
            StartError = ex.Message;
            Dispose();
            return false;
        }
    }

    private void RegisterNotifications(IXbdmNotificationSession session)
    {
        void Register(int code) => session.Notify((uint)code, OnNotification);

        Register(XbdmDebugConstants.DmDebugStr);
        Register(XbdmDebugConstants.DmRip);
        Register(XbdmDebugConstants.DmAssert);
        Register(XbdmDebugConstants.DmExec);
        Register(XbdmDebugConstants.DmBreak);
        Register(XbdmDebugConstants.DmException);
        Register(XbdmDebugConstants.DmDataBreak);
    }

    private void OnNotification(uint notification, object? data)
    {
        var code = (int)(notification & XbdmDebugConstants.NotificationMask);

        switch (code)
        {
            case XbdmDebugConstants.DmExec:
                HandleExec(data);
                break;
            case XbdmDebugConstants.DmDebugStr when data is XbdmDebugStringNotification dbg:
                HandleDebugString(dbg);
                break;
            case XbdmDebugConstants.DmAssert when data is XbdmDebugStringNotification assert:
                _ = HandleAssertAsync(assert);
                break;
            case XbdmDebugConstants.DmRip when data is XbdmDebugStringNotification rip:
                _ = HandleRipAsync(rip);
                break;
            case XbdmDebugConstants.DmBreak when data is XbdmBreakNotification brk:
                Log($"Break: 0x{(uint)brk.Address:x8} 0x{brk.ThreadId:x8}\r\n");
                _ = HandleExceptionUiAsync(brk.ThreadId, WatsonExceptionCodes.Breakpoint, brk.Address, false, 0);
                break;
            case XbdmDebugConstants.DmDataBreak when data is XbdmDataBreakNotification db:
                Log($"Databreak: 0x{(uint)db.Address:x8} 0x{db.ThreadId:x8} 0x{db.BreakType:x8} 0x{(uint)db.DataAddress:x8}\r\n");
                _ = HandleExceptionUiAsync(db.ThreadId, WatsonExceptionCodes.Breakpoint, db.Address, false, 0);
                break;
            case XbdmDebugConstants.DmException when data is XbdmExceptionNotification ex:
                if ((ex.Flags & XbdmDebugConstants.DmExceptFirstChance) != 0)
                    return;
                Log($"Exception: 0x{ex.ThreadId:x8} 0x{ex.Code:x8} 0x{(uint)ex.Address:x8} 0x{ex.Flags:x8} 0x{ex.Information0:x8} 0x{ex.Information1:x8}\r\n");
                _ = HandleExceptionUiAsync(ex.ThreadId, ex.Code, ex.Address, ex.Information0 != 0, ex.Information1);
                break;
            default:
                if (data is XbdmDebugStringNotification unknown)
                    Log($"{unknown.Text}\r\n");
                break;
        }
    }

    private void HandleExec(object? data)
    {
        if (!_connectedLogged)
        {
            _connectedLogged = true;
            ProbeKernelDebugSupport();
            _sink.OnXboxConnected();
            Log("xbwatson: Connection to Xbox successful.\r\n");
            return;
        }

        if (data is int execState && execState == XbdmDebugConstants.DmnExecReboot)
            Log("xbwatson: Xbox is restarting.\r\n");
    }

    private void HandleDebugString(XbdmDebugStringNotification dbg)
    {
        if (WatsonKernelDebug.IsKernelDebugString(dbg.ThreadId) && !KernelDebugOutputEnabled)
            return;

        var text = dbg.Text;
        if (string.IsNullOrEmpty(text))
            return;

        text = TruncateNotificationText(text);
        Log(WatsonLogBuffer.FormatDebugStringForLog(text));
    }

    public bool TrySetKernelDebugOutput(bool enabled)
    {
        lock (_gate)
        {
            if (_debug == null || !KernelDebugProbed)
                return false;

            if (enabled && !KernelDebugAvailable)
                return false;

            if (!WatsonKernelDebug.TrySetDpctrace(_debug, enabled))
                return false;

            KernelDebugOutputEnabled = enabled;
            return true;
        }
    }

    private void ProbeKernelDebugSupport()
    {
        lock (_gate)
        {
            KernelDebugProbed = true;
            KernelDebugAvailable = false;

            if (_debug == null)
                return;

            if (WatsonKernelDebug.TryProbeDpctraceSupport(_debug, KernelDebugOutputEnabled, out var available))
                KernelDebugAvailable = available;
        }
    }

    private static string TruncateNotificationText(string text) =>
        text.Length <= MaxNotificationStringLength
            ? text
            : text[..MaxNotificationStringLength];

    private async Task HandleAssertAsync(XbdmDebugStringNotification assert)
    {
        var logLine = TruncateNotificationText(assert.Text);
        var newline = logLine.IndexOf('\n');
        if (newline >= 0)
            logLine = logLine[..newline];
        Log($"Assert: {logLine}\r\n");

        var launchPath = string.Empty;
        lock (_gate)
        {
            try
            {
                if (_debug != null)
                    launchPath = _debug.GetXbeInfo(string.Empty).LaunchPath;
            }
            catch
            {
            }
        }

        var evt = new WatsonAssertEvent
        {
            AssertText = assert.Text,
            ThreadId = assert.ThreadId,
            ConsoleName = _consoleName,
            LaunchPath = launchPath,
        };

        var choice = await _sink.OnAssertAsync(evt);
        lock (_gate)
        {
            if (_debug != null)
                WatsonActionHandler.HandleAssertChoice(_debug, assert.ThreadId, choice);
        }
    }

    private async Task HandleRipAsync(XbdmDebugStringNotification rip)
    {
        Log($"RIP: {TruncateNotificationText(rip.Text)}\r\n");
        var evt = new WatsonRipEvent
        {
            RipText = rip.Text,
            ThreadId = rip.ThreadId,
            ConsoleName = _consoleName,
        };

        var choice = await _sink.OnRipAsync(evt);
        lock (_gate)
        {
            if (_debug != null)
                WatsonActionHandler.HandleRipChoice(_debug, rip.ThreadId, choice);
        }
    }

    private async Task HandleExceptionUiAsync(
        uint threadId,
        uint code,
        nuint address,
        bool writeViolation,
        uint faultAddress)
    {
        var evt = new WatsonExceptionEvent
        {
            ThreadId = threadId,
            Code = code,
            Address = address,
            WriteViolation = writeViolation,
            FaultAddress = faultAddress,
            ConsoleName = _consoleName,
        };

        var choice = await _sink.OnExceptionAsync(evt);
        lock (_gate)
        {
            if (_debug != null)
                WatsonActionHandler.HandleExceptionChoice(_debug, threadId, choice);
        }
    }

    public bool TrySaveCrashDump(uint threadId, uint eventType, uint eventCode, bool writeViolation, uint avAddress, string? ripText, string path)
    {
        lock (_gate)
        {
            if (_debug == null || _connection == null)
                return false;

            var info = BreakInfoCollector.Collect(
                _debug,
                _consoleName,
                threadId,
                eventType,
                eventCode,
                writeViolation,
                avAddress,
                ripText,
                msg => _sink.OnWarning(msg));

            if (info == null)
                return false;

            using var stream = File.Create(path);
            BreakInfoWriter.Write(stream, info);
            return true;
        }
    }

    private void Log(string text)
    {
        if (TimestampsEnabled)
            text = WatsonLogFormatter.WithTimestamp(text);
        _sink.OnLog(text);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_notifySession != null)
            {
                _debug?.CloseNotificationSession(_notifySession);
                _notifySession = null;
            }

            if (_connection != null)
            {
                try
                {
                    _connection.UseSharedConnection(false);
                }
                catch
                {
                }

                _connection.Dispose();
                _connection = null;
            }

            _debug = null;
        }
    }
}
