using System.Runtime.InteropServices;

namespace Rxdk.Xbdm;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct XbdmFloatingSaveArea
{
    public uint ControlWord;
    public uint StatusWord;
    public uint TagWord;
    public uint ErrorOffset;
    public uint ErrorSelector;
    public uint DataOffset;
    public uint DataSelector;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbdmDebugConstants.SizeOf80387Registers)]
    public byte[] RegisterArea;

    public uint Cr0NpxState;

    public XbdmFloatingSaveArea()
    {
        RegisterArea = new byte[XbdmDebugConstants.SizeOf80387Registers];
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct XbdmContext
{
    public uint ContextFlags;
    public uint Dr0;
    public uint Dr1;
    public uint Dr2;
    public uint Dr3;
    public uint Dr6;
    public uint Dr7;
    public XbdmFloatingSaveArea FloatSave;
    public uint SegGs;
    public uint SegFs;
    public uint SegEs;
    public uint SegDs;
    public uint Edi;
    public uint Esi;
    public uint Ebx;
    public uint Edx;
    public uint Ecx;
    public uint Eax;
    public uint Ebp;
    public uint Eip;
    public uint SegCs;
    public uint EFlags;
    public uint Esp;
    public uint SegSs;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbdmDebugConstants.MaximumSupportedExtension)]
    public byte[] ExtendedRegisters;

    public XbdmContext()
    {
        FloatSave = new XbdmFloatingSaveArea();
        ExtendedRegisters = new byte[XbdmDebugConstants.MaximumSupportedExtension];
    }
}
