using System.Runtime.InteropServices;
using Rxdk.Xbdm;

namespace Rxdk.XbWatson.Core.BreakInfo;

public sealed class WatsonThreadBreakInfo
{
    public bool IsValid { get; set; }
    public uint ThreadId { get; set; }
    public uint StackBase { get; set; }
    public uint StackSize { get; set; }
    public byte[]? StackBytes { get; set; }
    public XbdmContext Context { get; set; } = new();
}

public sealed class WatsonBreakInfo
{
    public uint EventType { get; set; }
    public uint BrokenThreadId { get; set; }
    public uint EventCode { get; set; }
    public bool WriteException { get; set; }
    public uint AvAddress { get; set; }
    public string RipText { get; set; } = "";
    public string XboxName { get; set; } = "";
    public DateTime SystemTime { get; set; }
    public string AppName { get; set; } = "";
    public List<NativeModLoad> Modules { get; set; } = new();
    public List<WatsonThreadBreakInfo> Threads { get; set; } = new();
    public uint FirstSectionBase { get; set; }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct NativeModLoad
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string Name;

    public uint BaseAddress;
    public uint Size;
    public uint TimeStamp;
    public uint CheckSum;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSystemTime
{
    public ushort Year;
    public ushort Month;
    public ushort DayOfWeek;
    public ushort Day;
    public ushort Hour;
    public ushort Minute;
    public ushort Second;
    public ushort Milliseconds;

    public static NativeSystemTime FromDateTime(DateTime value) => new()
    {
        Year = (ushort)value.Year,
        Month = (ushort)value.Month,
        DayOfWeek = (ushort)value.DayOfWeek,
        Day = (ushort)value.Day,
        Hour = (ushort)value.Hour,
        Minute = (ushort)value.Minute,
        Second = (ushort)value.Second,
        Milliseconds = (ushort)value.Millisecond,
    };
}
