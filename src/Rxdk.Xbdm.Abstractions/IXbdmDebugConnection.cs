namespace Rxdk.Xbdm;

/// <summary>
/// Xbox debug monitor APIs from <c>xboxdbg.h</c> (breakpoints, threads, memory, notifications).
/// </summary>
public interface IXbdmDebugConnection
{
    void UseSharedConnection(bool enable);
    void SetConnectionTimeout(TimeSpan connectTimeout, TimeSpan conversationTimeout);

    // Notifications
    IXbdmNotificationSession OpenNotificationSession(uint flags);
    void CloseNotificationSession(IXbdmNotificationSession session);

    // Breakpoints
    void SetBreakpoint(nuint address);
    void RemoveBreakpoint(nuint address);
    void SetInitialBreakpoint();
    void SetDataBreakpoint(nuint address, uint breakType, uint size);
    uint GetBreakpointType(nuint address);

    // Execution
    void Go();
    void Stop();
    void HaltThread(uint threadId);
    void ContinueThread(uint threadId, bool exception);
    void SetupFunctionCall(uint threadId);
    void ConnectDebugger(bool connect);
    void StopOn(uint stopFlags, bool stop);
    void Reboot(uint flags);

    // Memory
    int GetMemory(nuint address, Span<byte> buffer);
    int SetMemory(nuint address, ReadOnlySpan<byte> data);

    // Threads
    IReadOnlyList<uint> GetThreadList();
    void GetThreadContext(uint threadId, ref XbdmContext context);
    void SetThreadContext(uint threadId, ref XbdmContext context);
    XbdmThreadStop? TryGetThreadStop(uint threadId);
    XbdmThreadInfo GetThreadInfo(uint threadId);
    void SuspendThread(uint threadId);
    void ResumeThread(uint threadId);

    // Title / XBE
    void SetTitle(string? directory, string title, string? commandLine = null);
    XbdmXbeInfo GetXbeInfo(string name);

    // Modules
    IReadOnlyList<XbdmModLoadNotification> WalkLoadedModules();
    IReadOnlyList<XbdmSectionLoadNotification> WalkModuleSections(string moduleName);
    string GetModuleLongName(string shortName);

    // Misc protocol
    XbdmXtlData GetXtlData();
    DateTime GetSystemTime();
    void SetConfigValue(uint index, uint value);
    void CapControl(string action);

    // Low-level protocol (for advanced callers / parity with DmSendCommand)
    int SendCommand(string command, Span<char> responseBuffer);
    string SendCommand(string command);
    void SendBinary(ReadOnlySpan<byte> data);
    int ReceiveBinary(Span<byte> buffer);
    uint ReceiveBinarySize();
    string ReceiveSocketLine();
    void DedicateConnection(string? handler);

    // Performance counters
    XbdmCountData QueryPerformanceCounter(string name, uint type);
    IReadOnlyList<XbdmCountInfo> WalkPerformanceCounters();
    void EnableGpuCounter(bool enable);

    // GPU snapshots
    byte[] PixelShaderSnapshot(uint x, uint y, uint flags, uint marker);
    byte[] VertexShaderSnapshot(uint first, uint last, uint flags, uint marker);
    byte[] MonitorFrameBuffer();
}

public interface IXbdmNotificationSession : IDisposable
{
    uint Flags { get; }
    void Notify(uint notificationMask, XbdmNotifyHandler handler);
    void RegisterNotificationProcessor(string type, XbdmExtNotifyHandler handler);
}
