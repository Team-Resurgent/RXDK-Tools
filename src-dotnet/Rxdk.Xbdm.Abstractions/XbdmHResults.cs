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
    public const int Dedicated = unchecked((int)0x02DB0005);
}
