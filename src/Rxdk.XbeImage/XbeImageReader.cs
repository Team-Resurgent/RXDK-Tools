using System.Buffers.Binary;
using System.Text;

namespace Rxdk.XbeImage;

internal static class XbeImageReader
{
    private const int EncryptedDigestOffset = 4;
    public const int BaseAddressOffset = EncryptedDigestOffset + XbeImageConstants.EncryptedSignatureSize;

    public const int HeaderSize = BaseAddressOffset + 116;
    public const int SectionSize = 56;
    public const int ImportDescriptorSize = 8;
    public const int LibraryVersionSize = 16;
    public const int TlsDirectorySize = 24;

    public static XbeImageHeader ReadHeader(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderSize)
        {
            throw new XbeImageException("Invalid or corrupt input file.");
        }

        if (BitConverter.ToUInt32(image.Slice(0, 4)) != XbeImageConstants.XbeImageSignature)
        {
            throw new XbeImageException("Invalid or corrupt input file.");
        }

        return new XbeImageHeader
        {
            Signature = ReadUInt32(image, 0),
            EncryptedDigest = image.Slice(EncryptedDigestOffset, XbeImageConstants.EncryptedSignatureSize).ToArray(),
            BaseAddress = ReadUInt32(image, BaseAddressOffset),
            SizeOfHeaders = ReadUInt32(image, BaseAddressOffset + 4),
            SizeOfImage = ReadUInt32(image, BaseAddressOffset + 8),
            SizeOfImageHeader = ReadUInt32(image, BaseAddressOffset + 12),
            TimeDateStamp = ReadUInt32(image, BaseAddressOffset + 16),
            Certificate = ReadUInt32(image, BaseAddressOffset + 20),
            NumberOfSections = ReadUInt32(image, BaseAddressOffset + 24),
            SectionHeaders = ReadUInt32(image, BaseAddressOffset + 28),
            InitFlags = ReadUInt32(image, BaseAddressOffset + 32),
            AddressOfEntryPoint = ReadUInt32(image, BaseAddressOffset + 36),
            TlsDirectory = ReadUInt32(image, BaseAddressOffset + 40),
            SizeOfStackCommit = ReadUInt32(image, BaseAddressOffset + 44),
            SizeOfHeapReserve = ReadUInt32(image, BaseAddressOffset + 48),
            SizeOfHeapCommit = ReadUInt32(image, BaseAddressOffset + 52),
            NtBaseOfDll = ReadUInt32(image, BaseAddressOffset + 56),
            NtSizeOfImage = ReadUInt32(image, BaseAddressOffset + 60),
            NtCheckSum = ReadUInt32(image, BaseAddressOffset + 64),
            NtTimeDateStamp = ReadUInt32(image, BaseAddressOffset + 68),
            DebugPathName = ReadUInt32(image, BaseAddressOffset + 72),
            DebugFileName = ReadUInt32(image, BaseAddressOffset + 76),
            DebugUnicodeFileName = ReadUInt32(image, BaseAddressOffset + 80),
            XboxKernelThunkData = ReadUInt32(image, BaseAddressOffset + 84),
            ImportDirectory = ReadUInt32(image, BaseAddressOffset + 88),
            NumberOfLibraryVersions = ReadUInt32(image, BaseAddressOffset + 92),
            LibraryVersions = ReadUInt32(image, BaseAddressOffset + 96),
            XboxKernelLibraryVersion = ReadUInt32(image, BaseAddressOffset + 100),
            XapiLibraryVersion = ReadUInt32(image, BaseAddressOffset + 104),
            MicrosoftLogo = ReadUInt32(image, BaseAddressOffset + 108),
            SizeOfMicrosoftLogo = ReadUInt32(image, BaseAddressOffset + 112),
        };
    }

    public static XbeImageCertificate? ReadCertificate(ReadOnlySpan<byte> image, uint certificateVa)
    {
        if (certificateVa == 0)
        {
            return null;
        }

        var certificateOffset = MapVirtualAddress(image, certificateVa, sizeof(uint));
        if (certificateOffset is null)
        {
            return null;
        }

        var offset = certificateOffset.Value;
        if (offset + 4 > image.Length)
        {
            return null;
        }

        var sizeOfCertificate = ReadUInt32(image, offset);
        if (sizeOfCertificate < 4 || offset + sizeOfCertificate > image.Length)
        {
            return null;
        }

        var certificateBytes = image.Slice(offset, (int)sizeOfCertificate);
        return ParseCertificate(certificateBytes);
    }

    private static XbeImageCertificate ParseCertificate(ReadOnlySpan<byte> certificateBytes)
    {
        var offset = 0;
        var certificate = new XbeImageCertificate
        {
            SizeOfCertificate = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes),
            TimeDateStamp = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[4..]),
            TitleId = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[8..]),
            TitleName = new char[XbeImageConstants.TitleNameLength],
            AlternateTitleIds = new uint[XbeImageConstants.AlternateTitleIdCount],
            LanKey = new byte[XbeImageConstants.CertificateKeyLength],
            SignatureKey = new byte[XbeImageConstants.CertificateKeyLength],
            AlternateSignatureKeysFlat = new byte[XbeImageConstants.AlternateTitleIdCount * XbeImageConstants.CertificateKeyLength],
        };

        offset = 12;
        for (var i = 0; i < XbeImageConstants.TitleNameLength; i++)
        {
            certificate.TitleName[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(certificateBytes[(offset + i * 2)..]);
        }

        offset += XbeImageConstants.TitleNameLength * 2;
        for (var i = 0; i < XbeImageConstants.AlternateTitleIdCount; i++)
        {
            certificate.AlternateTitleIds[i] = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[(offset + i * 4)..]);
        }

        offset += XbeImageConstants.AlternateTitleIdCount * 4;
        certificate.AllowedMediaTypes = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[offset..]);
        offset += 4;
        certificate.GameRegion = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[offset..]);
        offset += 4;
        certificate.GameRatings = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[offset..]);
        offset += 4;
        certificate.DiskNumber = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[offset..]);
        offset += 4;
        certificate.Version = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[offset..]);
        offset += 4;
        certificateBytes.Slice(offset, XbeImageConstants.CertificateKeyLength).CopyTo(certificate.LanKey);
        offset += XbeImageConstants.CertificateKeyLength;
        certificateBytes.Slice(offset, XbeImageConstants.CertificateKeyLength).CopyTo(certificate.SignatureKey);
        offset += XbeImageConstants.CertificateKeyLength;
        certificateBytes.Slice(offset, certificate.AlternateSignatureKeysFlat.Length).CopyTo(certificate.AlternateSignatureKeysFlat);
        offset += certificate.AlternateSignatureKeysFlat.Length;

        if (offset + 4 <= certificateBytes.Length)
        {
            certificate.OriginalSizeOfCertificate = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[offset..]);
            offset += 4;
        }

        if (offset + 4 <= certificateBytes.Length)
        {
            certificate.OnlineServiceName = BinaryPrimitives.ReadUInt32LittleEndian(certificateBytes[offset..]);
        }

        return certificate;
    }

    public static XbeImageLibraryVersion[] ReadLibraryVersions(ReadOnlySpan<byte> image, uint libraryVersionsVa, uint count)
    {
        if (count == 0)
        {
            return [];
        }

        var dataOffset = MapVirtualAddress(image, libraryVersionsVa, (int)(count * LibraryVersionSize));
        if (dataOffset is null)
        {
            return [];
        }

        var versions = new XbeImageLibraryVersion[count];
        for (var i = 0; i < count; i++)
        {
            var offset = dataOffset.Value + i * LibraryVersionSize;
            versions[i] = BytesToStruct<XbeImageLibraryVersion>(image.Slice(offset, LibraryVersionSize));
        }

        return versions;
    }

    public static XbeImageSection[] ReadSections(ReadOnlySpan<byte> image, uint sectionHeadersVa, uint count)
    {
        if (count == 0)
        {
            return [];
        }

        var dataOffset = MapVirtualAddress(image, sectionHeadersVa, (int)(count * SectionSize));
        if (dataOffset is null)
        {
            return [];
        }

        var sections = new XbeImageSection[count];
        for (var i = 0; i < count; i++)
        {
            var offset = dataOffset.Value + i * SectionSize;
            sections[i] = BytesToStruct<XbeImageSection>(image.Slice(offset, SectionSize));
        }

        return sections;
    }

    public static ImageTlsDirectory32? ReadTlsDirectory(ReadOnlySpan<byte> image, uint tlsDirectoryVa)
    {
        if (tlsDirectoryVa == 0)
        {
            return null;
        }

        var offset = MapVirtualAddress(image, tlsDirectoryVa, TlsDirectorySize);
        if (offset is null)
        {
            return null;
        }

        return BytesToStruct<ImageTlsDirectory32>(image.Slice(offset.Value, TlsDirectorySize));
    }

    public static List<string> ReadImportModuleNames(ReadOnlySpan<byte> image, uint importDirectoryVa)
    {
        var moduleNames = new List<string>();
        if (importDirectoryVa == 0)
        {
            return moduleNames;
        }

        var rawImportDescriptor = importDirectoryVa;
        while (true)
        {
            var descriptorOffset = MapVirtualAddress(image, rawImportDescriptor, ImportDescriptorSize);
            if (descriptorOffset is null)
            {
                break;
            }

            var imageThunkData = ReadUInt32(image, descriptorOffset.Value);
            if (imageThunkData == 0)
            {
                break;
            }

            var imageNameVa = ReadUInt32(image, descriptorOffset.Value + 4);
            var nameOffset = MapVirtualAddress(image, imageNameVa, 2);
            if (nameOffset is null)
            {
                break;
            }

            moduleNames.Add(ReadNullTerminatedWideString(image, nameOffset.Value));
            rawImportDescriptor += (uint)ImportDescriptorSize;
        }

        return moduleNames;
    }

    public static int? MapVirtualAddress(ReadOnlySpan<byte> image, uint virtualAddress, int numberOfBytes)
    {
        if (image.Length < HeaderSize)
        {
            return null;
        }

        var baseAddress = ReadUInt32(image, BaseAddressOffset);
        var sizeOfHeaders = ReadUInt32(image, BaseAddressOffset + 4);

        if (virtualAddress >= baseAddress &&
            virtualAddress + (uint)numberOfBytes <= baseAddress + sizeOfHeaders)
        {
            var fileOffset = (int)(virtualAddress - baseAddress);
            return fileOffset + numberOfBytes <= image.Length ? fileOffset : null;
        }

        var numberOfSections = ReadUInt32(image, BaseAddressOffset + 24);
        if (numberOfSections == 0)
        {
            return null;
        }

        var sectionHeadersVa = ReadUInt32(image, BaseAddressOffset + 28);
        var sectionTableOffset = sectionHeadersVa - baseAddress;
        if (sectionTableOffset >= image.Length ||
            sectionTableOffset + numberOfSections * SectionSize > image.Length)
        {
            return null;
        }

        for (var index = 0; index < numberOfSections; index++)
        {
            var sectionOffset = (int)sectionTableOffset + index * SectionSize;
            var sectionVirtualAddress = ReadUInt32(image, sectionOffset + 4);
            var sizeOfRawData = ReadUInt32(image, sectionOffset + 16);

            if (virtualAddress >= sectionVirtualAddress &&
                virtualAddress + (uint)numberOfBytes <= sectionVirtualAddress + sizeOfRawData)
            {
                var pointerToRawData = ReadUInt32(image, sectionOffset + 12);
                var fileOffset = (int)(pointerToRawData + (virtualAddress - sectionVirtualAddress));
                return fileOffset + numberOfBytes <= image.Length ? fileOffset : null;
            }
        }

        return null;
    }

    public static string ReadAnsiStringAtVirtualAddress(ReadOnlySpan<byte> image, uint virtualAddress)
    {
        var offset = MapVirtualAddress(image, virtualAddress, 1);
        if (offset is null)
        {
            return string.Empty;
        }

        return ReadNullTerminatedAnsiString(image, offset.Value);
    }

    public static string ReadSectionName(ReadOnlySpan<byte> image, uint baseAddress, uint sectionNameVa)
    {
        var offset = (int)(sectionNameVa - baseAddress);
        if (offset < 0 || offset >= image.Length)
        {
            return string.Empty;
        }

        return ReadNullTerminatedAnsiString(image, offset);
    }

    public static string ReadCertificateTitleName(XbeImageCertificate certificate)
    {
        if (certificate.TitleName is null || certificate.TitleName.Length == 0)
        {
            return string.Empty;
        }

        return new string(certificate.TitleName).TrimEnd('\0');
    }

    public static byte[] ReadCertificateKey(byte[] flatKeys, int index)
    {
        var key = new byte[XbeImageConstants.CertificateKeyLength];
        Buffer.BlockCopy(
            flatKeys,
            index * XbeImageConstants.CertificateKeyLength,
            key,
            0,
            XbeImageConstants.CertificateKeyLength);
        return key;
    }

    private static T BytesToStruct<T>(ReadOnlySpan<byte> bytes) where T : struct
    {
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes.ToArray(), System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var value = System.Runtime.InteropServices.Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject())!;
            return value;
        }
        finally
        {
            handle.Free();
        }
    }

    private static string ReadNullTerminatedAnsiString(ReadOnlySpan<byte> image, int offset)
    {
        var end = offset;
        while (end < image.Length && image[end] != 0)
        {
            end++;
        }

        return Encoding.Latin1.GetString(image.Slice(offset, end - offset));
    }

    private static string ReadNullTerminatedWideString(ReadOnlySpan<byte> image, int offset)
    {
        var end = offset;
        while (end + 1 < image.Length)
        {
            if (image[end] == 0 && image[end + 1] == 0)
            {
                break;
            }

            end += 2;
        }

        return Encoding.Unicode.GetString(image.Slice(offset, end - offset));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> image, int offset) =>
        BitConverter.ToUInt32(image.Slice(offset, 4));
}
