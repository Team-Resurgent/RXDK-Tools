using System.Runtime.InteropServices;

namespace Rxdk.XbeImage;

public static class XbeImageConstants
{
    public const uint XbeImageSignature = 0x48454258;
    public const uint StandardBaseAddress = 0x0001_0000;
    public const int DigestLength = 20;
    public const int EncryptedSignatureSize = 256;
    public const int TitleNameLength = 40;
    public const int AlternateTitleIdCount = 16;
    public const int CertificateKeyLength = 16;
    public const int LibraryVersionNameLength = 8;

    public const uint GameRegionNa = 0x0000_0001;
    public const uint GameRegionJapan = 0x0000_0002;
    public const uint GameRegionRestOfWorld = 0x0000_0004;
    public const uint GameRegionManufacturing = 0x8000_0000;

    public const uint MediaTypeHardDisk = 0x0000_0001;
    public const uint MediaTypeDvdCd = 0x0000_0004;
    public const uint MediaTypeMediaBoard = 0x0000_0200;

    public const uint SectionWriteable = 0x0000_0001;
    public const uint SectionPreload = 0x0000_0002;
    public const uint SectionExecutable = 0x0000_0004;
    public const uint SectionInsertFile = 0x0000_0008;
    public const uint SectionHeadPageReadonly = 0x0000_0010;
    public const uint SectionTailPageReadonly = 0x0000_0020;

    public const uint XinitMountUtilityDrive = 0x0000_0001;
    public const uint XinitFormatUtilityDrive = 0x0000_0002;
    public const uint XinitLimitDevkitMemory = 0x0000_0004;
    public const uint XinitNoSetupHardDisk = 0x0000_0008;
    public const uint XinitDontModifyHardDisk = 0x0000_0010;
    public const uint XinitUtilityDriveClusterSizeMask = 0xC000_0000;
    public const uint XinitUtilityDriveClusterSizeShift = 30;
    public const uint XinitUtilityDrive16KClusterSize = 0x0000_0000;
    public const uint XinitUtilityDrive32KClusterSize = 0x4000_0000;
    public const uint XinitUtilityDrive64KClusterSize = 0x8000_0000;

    public const uint MaxUlong = 0xFFFF_FFFF;
    public const int VerProductBuild = 4400;
    public const int CertificateOnlineServiceNameOffset = 468;

    public const int PageSize = 0x1000;
    public const int InsertFileSectionAlignment = 32;
    public const uint MaximumImageSize = 0x8000_0000;

    public const ushort ImageDosSignature = 0x5A4D;
    public const uint ImageNtSignature = 0x0000_4550;
    public const ushort ImageNtOptionalHdr32Magic = 0x10B;
    public const ushort ImageFileMachineI386 = 0x014C;
    public const ushort ImageSubsystemXbox = 14;
    public const ushort ImageSubsystemWindowsCui = 3;

    public const uint ImageScnMemDiscardable = 0x0200_0000;
    public const uint ImageScnMemWrite = 0x8000_0000;
    public const uint ImageScnMemPreload = 0x0008_0000;

    public const int ImageDirectoryEntryImport = 1;
    public const int ImageDirectoryEntryBoundImport = 11;
    public const int ImageDirectoryEntryBaseReloc = 5;
    public const int ImageDirectoryEntryTls = 9;

    public const int ImageRelBasedAbsolute = 0;
    public const int ImageRelBasedHigh = 1;
    public const int ImageRelBasedLow = 2;
    public const int ImageRelBasedHighLow = 3;
    public const int ImageRelBasedHighAdj = 4;
    public const int ImageRelBasedSection = 6;
    public const int ImageRelBasedRel32 = 7;

    public const int ImageSizeofShortName = 8;
    public const uint ImageOrdinalFlag32 = unchecked((uint)0x8000_0000);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XbeImageHeader
{
    public uint Signature;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.EncryptedSignatureSize)]
    public byte[] EncryptedDigest;
    public uint BaseAddress;
    public uint SizeOfHeaders;
    public uint SizeOfImage;
    public uint SizeOfImageHeader;
    public uint TimeDateStamp;
    public uint Certificate;
    public uint NumberOfSections;
    public uint SectionHeaders;
    public uint InitFlags;
    public uint AddressOfEntryPoint;
    public uint TlsDirectory;
    public uint SizeOfStackCommit;
    public uint SizeOfHeapReserve;
    public uint SizeOfHeapCommit;
    public uint NtBaseOfDll;
    public uint NtSizeOfImage;
    public uint NtCheckSum;
    public uint NtTimeDateStamp;
    public uint DebugPathName;
    public uint DebugFileName;
    public uint DebugUnicodeFileName;
    public uint XboxKernelThunkData;
    public uint ImportDirectory;
    public uint NumberOfLibraryVersions;
    public uint LibraryVersions;
    public uint XboxKernelLibraryVersion;
    public uint XapiLibraryVersion;
    public uint MicrosoftLogo;
    public uint SizeOfMicrosoftLogo;

    public static int BaseAddressFieldOffset =>
        Marshal.OffsetOf<XbeImageHeader>(nameof(BaseAddress)).ToInt32();
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XbeImageCertificate
{
    public uint SizeOfCertificate;
    public uint TimeDateStamp;
    public uint TitleId;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.TitleNameLength)]
    public char[] TitleName;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.AlternateTitleIdCount)]
    public uint[] AlternateTitleIds;
    public uint AllowedMediaTypes;
    public uint GameRegion;
    public uint GameRatings;
    public uint DiskNumber;
    public uint Version;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.CertificateKeyLength)]
    public byte[] LanKey;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.CertificateKeyLength)]
    public byte[] SignatureKey;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.AlternateTitleIdCount * XbeImageConstants.CertificateKeyLength)]
    public byte[] AlternateSignatureKeysFlat;
    public uint OriginalSizeOfCertificate;
    public uint OnlineServiceName;

    public static int Size => 472;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XbeImageImportDescriptor
{
    public uint ImageThunkData;
    public uint ImageName;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XbeImageSection
{
    public uint SectionFlags;
    public uint VirtualAddress;
    public uint VirtualSize;
    public uint PointerToRawData;
    public uint SizeOfRawData;
    public uint SectionName;
    public uint SectionReferenceCount;
    public uint HeadSharedPageReferenceCount;
    public uint TailSharedPageReferenceCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.DigestLength)]
    public byte[] SectionDigest;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XbeImageLibraryVersion
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.LibraryVersionNameLength)]
    public byte[] LibraryName;
    public ushort MajorVersion;
    public ushort MinorVersion;
    public ushort BuildVersion;
    public ushort VersionFlags;

    public ushort QfeVersion => (ushort)(VersionFlags & 0x1FFF);
    public ushort ApprovedLibrary => (ushort)((VersionFlags >> 13) & 0x3);
    public bool DebugBuild => (VersionFlags & 0x8000) != 0;

    public void SetApprovedLibrary(int approvalLevel)
    {
        VersionFlags = (ushort)((VersionFlags & ~0x6000) | (((ushort)approvalLevel & 0x3) << 13));
    }

    public static int Size => Marshal.SizeOf<XbeImageLibraryVersion>();
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageDosHeader
{
    public ushort e_magic;
    public ushort e_cblp;
    public ushort e_cp;
    public ushort e_crlc;
    public ushort e_cparhdr;
    public ushort e_minalloc;
    public ushort e_maxalloc;
    public ushort e_ss;
    public ushort e_sp;
    public ushort e_csum;
    public ushort e_ip;
    public ushort e_cs;
    public ushort e_lfarlc;
    public ushort e_ovno;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public ushort[] e_res;
    public ushort e_oemid;
    public ushort e_oeminfo;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
    public ushort[] e_res2;
    public int e_lfanew;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageFileHeader
{
    public ushort Machine;
    public ushort NumberOfSections;
    public uint TimeDateStamp;
    public uint PointerToSymbolTable;
    public uint NumberOfSymbols;
    public ushort SizeOfOptionalHeader;
    public ushort Characteristics;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageDataDirectory
{
    public uint VirtualAddress;
    public uint Size;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageOptionalHeader32
{
    public ushort Magic;
    public byte MajorLinkerVersion;
    public byte MinorLinkerVersion;
    public uint SizeOfCode;
    public uint SizeOfInitializedData;
    public uint SizeOfUninitializedData;
    public uint AddressOfEntryPoint;
    public uint BaseOfCode;
    public uint BaseOfData;
    public uint ImageBase;
    public uint SectionAlignment;
    public uint FileAlignment;
    public ushort MajorOperatingSystemVersion;
    public ushort MinorOperatingSystemVersion;
    public ushort MajorImageVersion;
    public ushort MinorImageVersion;
    public ushort MajorSubsystemVersion;
    public ushort MinorSubsystemVersion;
    public uint Win32VersionValue;
    public uint SizeOfImage;
    public uint SizeOfHeaders;
    public uint CheckSum;
    public ushort Subsystem;
    public ushort DllCharacteristics;
    public uint SizeOfStackReserve;
    public uint SizeOfStackCommit;
    public uint SizeOfHeapReserve;
    public uint SizeOfHeapCommit;
    public uint LoaderFlags;
    public uint NumberOfRvaAndSizes;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageNtHeaders32
{
    public uint Signature;
    public ImageFileHeader FileHeader;
    public ImageOptionalHeader32 OptionalHeader;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageSectionHeader
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = XbeImageConstants.ImageSizeofShortName)]
    public byte[] Name;
    public uint VirtualSizeUnion;
    public uint VirtualAddress;
    public uint SizeOfRawData;
    public uint PointerToRawData;
    public uint PointerToRelocations;
    public uint PointerToLinenumbers;
    public ushort NumberOfRelocations;
    public ushort NumberOfLinenumbers;
    public uint Characteristics;

    public uint VirtualSize
    {
        get => VirtualSizeUnion;
        set => VirtualSizeUnion = value;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageImportDescriptor
{
    public uint OriginalFirstThunk;
    public uint TimeDateStamp;
    public uint ForwarderChain;
    public uint Name;
    public uint FirstThunk;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageBaseRelocation
{
    public uint VirtualAddress;
    public uint SizeOfBlock;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageTlsDirectory32
{
    public uint StartAddressOfRawData;
    public uint EndAddressOfRawData;
    public uint AddressOfIndex;
    public uint AddressOfCallbacks;
    public uint SizeOfZeroFill;
    public uint Characteristics;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BSafePubKeyHeader
{
    public uint Magic;
    public uint KeyLen;
    public uint BitLen;
    public uint DataLen;
    public uint PubExp;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BSafePrvKeyHeader
{
    public uint Magic;
    public uint KeyLen;
    public uint BitLen;
    public uint DataLen;
    public uint PubExp;
}
