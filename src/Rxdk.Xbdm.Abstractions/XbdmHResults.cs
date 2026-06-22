namespace Rxdk.Xbdm;

/// <summary>HRESULT values from the Xbox Debug Monitor (FACILITY_XBDM = 0x2db).</summary>
public static class XbdmHResults
{
    private const int Facility = 0x2db;

    public static int Success(int code) => unchecked((int)(0x00000000 | (Facility << 16) | code));
    public static int Error(int code) => unchecked((int)(0x80000000 | (Facility << 16) | code));

    public const int NoErr = unchecked((int)0x02DB0000);
    public const int Connected = unchecked((int)0x02DB0001);
    public const int Multiresponse = unchecked((int)0x02DB0002);
    public const int Binresponse = unchecked((int)0x02DB0003);
    public const int ReadyForBin = unchecked((int)0x02DB0004);

    public static bool IsSuccess(int hresult) => (hresult & unchecked((int)0x80000000)) == 0;

    public const int CannotConnect = unchecked((int)0x82DB0100);
    public const int CannotAccess = unchecked((int)0x82DB000E);
    public const int NoSuchFile = unchecked((int)0x82DB0002);
    public const int MaxConnect = unchecked((int)0x82DB0001);
    public const int ConnectionLost = unchecked((int)0x82DB0101);
    public const int FileError = unchecked((int)0x82DB0103);
    public const int EndOfList = unchecked((int)0x82DB0104);
    public const int BufferTooSmall = unchecked((int)0x82DB0105);
    public const int NoXboxName = unchecked((int)0x82DB0108);
    public const int AlreadyExists = unchecked((int)0x82DB000A);
    public const int InvalidCmd = unchecked((int)0x82DB0007);
    public const int NotLocked = unchecked((int)0x82DB0014);
    public const int KeyExchange = unchecked((int)0x82DB0015);
    public const int MemUnmapped = unchecked((int)0x82DB0004);
    public const int MemSetIncomplete = unchecked((int)0x82DB0107);
    public const int ClockNotSet = unchecked((int)0x82DB0006);
    public const int NotStopped = unchecked((int)0x82DB0008);
    public const int NotDebuggable = unchecked((int)0x82DB0010);
    public const int Dedicated = unchecked((int)0x02DB0005);

    private static readonly IReadOnlyDictionary<int, string> Descriptions = new Dictionary<int, string>
    {
        [NoErr] = "NoErr — command succeeded",
        [Connected] = "Connected — session established",
        [Multiresponse] = "Multiresponse — multi-line response follows",
        [Binresponse] = "Binresponse — binary payload follows",
        [ReadyForBin] = "ReadyForBin — ready for binary transfer",
        [Dedicated] = "Dedicated — dedicated connection mode",
        [Error(0)] = "Undefined — unspecified XBDM error",
        [MaxConnect] = "MaxConnect — too many connections",
        [NoSuchFile] = "NoSuchFile — file or launch path not found",
        [Error(3)] = "NoModule — module not loaded",
        [MemUnmapped] = "MemUnmapped — address not mapped",
        [Error(5)] = "NoThread — thread not found",
        [ClockNotSet] = "ClockNotSet — console clock not set",
        [InvalidCmd] = "InvalidCmd — command not supported in current state",
        [NotStopped] = "NotStopped — title must be stopped",
        [Error(9)] = "MustCopy — copy required instead of move",
        [AlreadyExists] = "AlreadyExists — object already exists",
        [Error(11)] = "DirNotEmpty — directory not empty",
        [Error(12)] = "BadFilename — invalid file name",
        [Error(13)] = "CannotCreate — cannot create file or directory",
        [CannotAccess] = "CannotAccess — access denied",
        [Error(15)] = "DeviceFull — storage device full",
        [NotDebuggable] = "NotDebuggable — debugger cannot attach",
        [Error(17)] = "BadCountType — invalid performance counter type",
        [Error(18)] = "CountUnavailable — performance counter unavailable",
        [NotLocked] = "NotLocked — security disabled; operation requires lock mode",
        [KeyExchange] = "KeyExchange — secure key exchange required",
        [Error(22)] = "MustBeDedicated — command requires dedicated connection",
        [CannotConnect] = "CannotConnect — could not connect to console",
        [ConnectionLost] = "ConnectionLost — connection lost",
        [FileError] = "FileError — file I/O or protocol error",
        [EndOfList] = "EndOfList — end of enumeration",
        [BufferTooSmall] = "BufferTooSmall — caller buffer too small",
        [Error(0x106)] = "NotXbeFile — not a valid XBE file",
        [MemSetIncomplete] = "MemSetIncomplete — memory write incomplete",
        [NoXboxName] = "NoXboxName — no default Xbox name configured",
        [Error(0x109)] = "NoErrorString — no error text available",
    };

    /// <summary>Human-readable name and hint for parity logs and UI.</summary>
    public static string Describe(int hresult) =>
        Descriptions.TryGetValue(hresult, out var text) ? text : DescribeUnknown(hresult);

    /// <summary>HRESULT plus description, optionally with exception detail.</summary>
    public static string Format(int hresult, string? detail = null)
    {
        var text = $"0x{hresult:x8} ({Describe(hresult)})";
        if (!string.IsNullOrWhiteSpace(detail))
            text += $" — {detail.Trim()}";
        return text;
    }

    private static string DescribeUnknown(int hresult)
    {
        if (((hresult >> 16) & 0x1FFF) != Facility)
            return $"HRESULT 0x{hresult:x8}";

        var code = hresult & 0xFFFF;
        if ((hresult & 0x80000000) != 0 && code >= 100)
            return $"Protocol/status error {code} — kit returned non-success status line";

        return code >= 0x8000
            ? $"XBDM success code {code & 0x7FFF}"
            : $"XBDM error {code}";
    }
}
