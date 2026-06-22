using System.Runtime.InteropServices;

namespace Rxdk.XboxDbgBridge.Interop;

internal static class DbgHelpNative
{
    internal const uint SymoptUndname = 0x00000002;
    internal const uint SymoptLoadLines = 0x00000010;
    internal const uint SymflagRegister = 0x00000008;
    internal const uint SymflagRegrel = 0x00000010;
    internal const int MaxSymName = 2000;
    internal static readonly IntPtr PseudoProcess = (IntPtr)1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ImageHlpLine64
    {
        public uint SizeOfStruct;
        public IntPtr Key;
        public uint LineNumber;
        public IntPtr FileName;
        public ulong Address;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ImageHlpModule64
    {
        public uint SizeOfStruct;
        public ulong BaseOfDll;
        public uint SizeOfImage;
        public uint TimeDateStamp;
        public uint CheckSum;
        public uint NumSyms;
        public uint SymType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ModuleName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ImageName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string LoadedImageName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string LoadedPdbName;
        public uint CvSig;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 780)]
        public string CvData;
        public uint PdbSig;
        public Guid PdbSig70;
        public uint PdbAge;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string PdbUnmatched;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DbgUnmatched;
        public bool LineNumbers;
        public bool GlobalSymbols;
        public bool TypeInfo;
    }

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymInitialize(IntPtr hProcess, string? userSearchPath, bool fInvadeProcess);

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymCleanup(IntPtr hProcess);

    [DllImport("dbghelp.dll", SetLastError = true)]
    internal static extern uint SymSetOptions(uint symOptions);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymSetSearchPath(IntPtr hProcess, string path);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    internal static extern ulong SymLoadModuleEx(
        IntPtr hProcess,
        IntPtr hFile,
        string imageName,
        string? symbolFile,
        ulong baseOfDll,
        uint dwSizeOfDll,
        IntPtr data,
        uint flags);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymFromName(IntPtr hProcess, string name, IntPtr symbol);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymFromAddr(IntPtr hProcess, ulong address, out ulong displacement, IntPtr symbol);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymGetLineFromName64(
        IntPtr hProcess,
        string? moduleName,
        string fileName,
        uint lineNumber,
        out uint displacement,
        ref ImageHlpLine64 line);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymGetLineFromAddr64(
        IntPtr hProcess,
        ulong address,
        out uint displacement,
        ref ImageHlpLine64 line);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymGetLineNext64(IntPtr hProcess, ref ImageHlpLine64 line);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymGetLinePrev64(IntPtr hProcess, ref ImageHlpLine64 line);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymGetModuleInfo64(IntPtr hProcess, ulong dwAddr, ref ImageHlpModule64 moduleInfo);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    internal static extern uint UnDecorateSymbolName(string name, System.Text.StringBuilder output, int maxString, uint flags);

    internal const uint UndnameNameOnly = 0x00001000;

    internal const uint TiGetSymtag = 3;
    internal const uint TiGetChildrencount = 5;
    internal const uint TiGetLength = 11;
    internal const uint TiGetType = 16;
    internal const uint TiFindchildren = 18;
    internal const uint TiGetOffset = 19;
    internal const uint TiGetSymname = 20;
    internal const uint TiGetCount = 24;
    internal const uint TiGetBaseType = 29;

    internal const uint SymTagArrayType = 3;
    internal const uint SymTagFunction = 5;
    internal const uint SymTagBlock = 6;
    internal const uint SymTagData = 7;
    internal const uint SymTagUdt = 11;

    internal const int SymbolInfoSize = 88;

    // SYMBOL_INFO field offsets for x64 / natural 8-byte alignment. Verified against live dbghelp
    // output (name at 84, Flags at 40 with RegRel=0x10, Tag at 72=SymTagData, Address at 56).
    // SizeOfStruct stays SymbolInfoSize (88); MaxNameLen must be written at 80 or names come back empty.
    internal const int SymInfoTypeIndex = 4;
    internal const int SymInfoSize = 28;
    internal const int SymInfoFlags = 40;
    internal const int SymInfoValue = 48;
    internal const int SymInfoAddress = 56;
    internal const int SymInfoRegister = 64;
    internal const int SymInfoTag = 72;
    internal const int SymInfoMaxNameLen = 80;
    internal const int SymInfoName = 84;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ImageHlpStackFrame
    {
        public ulong InstructionOffset;
        public ulong ReturnOffset;
        public ulong FrameOffset;
        public ulong StackOffset;
        public ulong BackingStoreOffset;
        public ulong FuncTableEntry;
        public ulong Param0;
        public ulong Param1;
        public ulong Param2;
        public ulong Param3;
        public ulong Reserved0;
        public ulong Reserved1;
        public ulong Reserved2;
        public ulong Reserved3;
        public ulong Reserved4;
        public bool Virtual;
        public uint Reserved5;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TiFindChildrenParams
    {
        public uint Count;
        public uint Start;
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymSetContext(IntPtr hProcess, ref ImageHlpStackFrame stackFrame, IntPtr scope);

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymGetTypeInfo(
        IntPtr hProcess,
        ulong modBase,
        uint typeId,
        uint getType,
        IntPtr pInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    internal delegate bool SymEnumSymbolsCallback(IntPtr symbolInfo, uint symbolSize, IntPtr userContext);

    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymEnumSymbols(
        IntPtr hProcess,
        ulong baseOfDll,
        string? mask,
        SymEnumSymbolsCallback enumSymbolsCallback,
        IntPtr userContext);
}
