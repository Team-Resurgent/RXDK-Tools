namespace Rxdk.Xbdm;

public sealed record XbdmBreakNotification(nuint Address, uint ThreadId);

public sealed record XbdmDataBreakNotification(
    nuint Address,
    uint ThreadId,
    uint BreakType,
    nuint DataAddress);

public sealed record XbdmDebugStringNotification(uint ThreadId, string Text);

public sealed record XbdmModLoadNotification(
    string Name,
    nuint BaseAddress,
    uint Size,
    uint TimeStamp,
    uint CheckSum,
    uint Flags);

public sealed record XbdmCreateThreadNotification(uint ThreadId, nuint StartAddress);

public sealed record XbdmExceptionNotification(
    uint ThreadId,
    uint Code,
    nuint Address,
    uint Flags,
    uint Information0,
    uint Information1);

public sealed record XbdmThreadStop(uint NotifiedReason, object? Data);

public sealed record XbdmThreadInfo(uint SuspendCount, uint Priority, nuint TlsBase);

public sealed record XbdmXbeInfo
{
    public string LaunchPath { get; set; } = "";
    public uint TimeStamp { get; set; }
    public uint CheckSum { get; set; }
    public uint StackSize { get; set; }

    public XbdmXbeInfo()
    {
    }

    public XbdmXbeInfo(string launchPath, uint timeStamp, uint checkSum, uint stackSize)
    {
        LaunchPath = launchPath;
        TimeStamp = timeStamp;
        CheckSum = checkSum;
        StackSize = stackSize;
    }
}

public sealed record XbdmXtlData(uint LastErrorOffset);

public sealed record XbdmCountData(ulong CountValue, ulong RateValue, uint CountType);

public sealed record XbdmCountInfo(string Name, uint Type);

public sealed record XbdmSectionLoadNotification(
    string Name,
    nuint BaseAddress,
    uint Size,
    ushort Index,
    ushort Flags);

public sealed record XbdmFiberNotification(uint FiberId, bool Create, nuint StartAddress);

public delegate void XbdmNotifyHandler(uint notification, object? data);

public delegate void XbdmExtNotifyHandler(string notificationLine);
