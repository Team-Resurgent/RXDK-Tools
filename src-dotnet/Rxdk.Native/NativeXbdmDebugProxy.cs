using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Native;

/// <summary>
/// Routes debugger APIs through the managed XBDM stack while file I/O stays on xbdm.dll.
/// </summary>
internal sealed class NativeXbdmDebugProxy : IXbdmDebugConnection, IDisposable
{
    private readonly string _consoleName;
    private readonly object _gate = new();
    private ManagedXbdmClient? _managedClient;
    private IXbdmConnection? _managedConnection;
    private IXbdmDebugConnection? _debug;

    internal NativeXbdmDebugProxy(string consoleName) => _consoleName = consoleName;

    private IXbdmDebugConnection Debug
    {
        get
        {
            lock (_gate)
            {
                if (_debug is not null)
                    return _debug;

                _managedClient = new ManagedXbdmClient();
                _managedClient.Initialize();
                _managedConnection = ConnectManaged(_managedClient, _consoleName);
                _debug = _managedConnection.Debug;
                _debug.UseSharedConnection(true);
                return _debug;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _managedConnection?.Dispose();
            _managedClient?.Dispose();
            _managedConnection = null;
            _managedClient = null;
            _debug = null;
        }
    }

    public void UseSharedConnection(bool enable) => Debug.UseSharedConnection(enable);

    public void SetConnectionTimeout(TimeSpan connectTimeout, TimeSpan conversationTimeout) =>
        Debug.SetConnectionTimeout(connectTimeout, conversationTimeout);

    public IXbdmNotificationSession OpenNotificationSession(uint flags) =>
        Debug.OpenNotificationSession(flags);

    public void CloseNotificationSession(IXbdmNotificationSession session) =>
        Debug.CloseNotificationSession(session);

    public void SetBreakpoint(nuint address) => Debug.SetBreakpoint(address);
    public void RemoveBreakpoint(nuint address) => Debug.RemoveBreakpoint(address);
    public void SetInitialBreakpoint() => Debug.SetInitialBreakpoint();
    public void SetDataBreakpoint(nuint address, uint breakType, uint size) =>
        Debug.SetDataBreakpoint(address, breakType, size);
    public uint GetBreakpointType(nuint address) => Debug.GetBreakpointType(address);
    public void Go() => Debug.Go();
    public void Stop() => Debug.Stop();
    public void HaltThread(uint threadId) => Debug.HaltThread(threadId);
    public void ContinueThread(uint threadId, bool exception) => Debug.ContinueThread(threadId, exception);
    public void SetupFunctionCall(uint threadId) => Debug.SetupFunctionCall(threadId);
    public void ConnectDebugger(bool connect) => Debug.ConnectDebugger(connect);
    public void StopOn(uint stopFlags, bool stop) => Debug.StopOn(stopFlags, stop);
    public void Reboot(uint flags) => Debug.Reboot(flags);
    public int GetMemory(nuint address, Span<byte> buffer) => Debug.GetMemory(address, buffer);
    public int SetMemory(nuint address, ReadOnlySpan<byte> data) => Debug.SetMemory(address, data);
    public IReadOnlyList<uint> GetThreadList() => Debug.GetThreadList();
    public void GetThreadContext(uint threadId, ref XbdmContext context) =>
        Debug.GetThreadContext(threadId, ref context);
    public void SetThreadContext(uint threadId, ref XbdmContext context) =>
        Debug.SetThreadContext(threadId, ref context);
    public XbdmThreadStop? TryGetThreadStop(uint threadId) => Debug.TryGetThreadStop(threadId);
    public XbdmThreadInfo GetThreadInfo(uint threadId) => Debug.GetThreadInfo(threadId);
    public void SuspendThread(uint threadId) => Debug.SuspendThread(threadId);
    public void ResumeThread(uint threadId) => Debug.ResumeThread(threadId);
    public void SetTitle(string? directory, string title, string? commandLine = null) =>
        Debug.SetTitle(directory, title, commandLine);
    public XbdmXbeInfo GetXbeInfo(string name) => Debug.GetXbeInfo(name);
    public IReadOnlyList<XbdmModLoadNotification> WalkLoadedModules() => Debug.WalkLoadedModules();
    public IReadOnlyList<XbdmSectionLoadNotification> WalkModuleSections(string moduleName) =>
        Debug.WalkModuleSections(moduleName);
    public string GetModuleLongName(string shortName) => Debug.GetModuleLongName(shortName);
    public XbdmXtlData GetXtlData() => Debug.GetXtlData();
    public DateTime GetSystemTime() => Debug.GetSystemTime();
    public void SetConfigValue(uint index, uint value) => Debug.SetConfigValue(index, value);
    public void CapControl(string action) => Debug.CapControl(action);
    public int SendCommand(string command, Span<char> responseBuffer) =>
        Debug.SendCommand(command, responseBuffer);
    public string SendCommand(string command) => Debug.SendCommand(command);
    public void SendBinary(ReadOnlySpan<byte> data) => Debug.SendBinary(data);
    public int ReceiveBinary(Span<byte> buffer) => Debug.ReceiveBinary(buffer);
    public uint ReceiveBinarySize() => Debug.ReceiveBinarySize();
    public string ReceiveSocketLine() => Debug.ReceiveSocketLine();
    public void DedicateConnection(string? handler) => Debug.DedicateConnection(handler);
    public XbdmCountData QueryPerformanceCounter(string name, uint type) =>
        Debug.QueryPerformanceCounter(name, type);
    public IReadOnlyList<XbdmCountInfo> WalkPerformanceCounters() => Debug.WalkPerformanceCounters();
    public void EnableGpuCounter(bool enable) => Debug.EnableGpuCounter(enable);
    public byte[] PixelShaderSnapshot(uint x, uint y, uint flags, uint marker) =>
        Debug.PixelShaderSnapshot(x, y, flags, marker);
    public byte[] VertexShaderSnapshot(uint first, uint last, uint flags, uint marker) =>
        Debug.VertexShaderSnapshot(first, last, flags, marker);
    public byte[] MonitorFrameBuffer() => Debug.MonitorFrameBuffer();

    private static IXbdmConnection ConnectManaged(ManagedXbdmClient client, string console)
    {
        var password = Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD")
            ?? Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD");
        return string.IsNullOrWhiteSpace(password)
            ? client.Connect(console)
            : client.Connect(console, new XbdmConnectOptions { AdminPassword = password });
    }
}
