namespace Rxdk.Xbdm;

public static class XbdmConstants
{
    public const int AbiVersion = 1;
    public const int MaxName = 256;
    public const int MaxPath = 260;
    public const int MaxDrives = 32;
    public const uint AttrDirectory = 0x10;
    public const uint AttrReadOnly = 0x01;
    public const uint AttrHidden = 0x02;
    /// <summary>Win32 FILE_ATTRIBUTE_NORMAL — shell uses this so SETFILEATTRIBUTES sends READONLY=0.</summary>
    public const uint AttrNormal = 0x80;

    public const uint PrivRead = 0x0001;
    public const uint PrivWrite = 0x0002;
    public const uint PrivControl = 0x0004;
    public const uint PrivConfigure = 0x0008;
    public const uint PrivManage = 0x0010;
    public const uint PrivAll = 0x001F;
    public const uint PrivInitial = 0x0003;

    public const int DebuggerPort = 0x2db;
}

public sealed record XbdmAbiInfo(uint AbiVersion, uint Build);

public sealed record XbdmDirEntry(
    string Name,
    ulong Size,
    uint Attributes,
    long ChangeTimeUnix,
    long? CreationTimeUnix = null);

public sealed record XbdmUser(string UserName, uint AccessPrivileges);
