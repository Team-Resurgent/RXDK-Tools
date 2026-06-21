using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Rxdk.XbeImage;

internal static class BinaryStructWriter
{
    public static void WriteXbeImageHeader(Span<byte> buffer, XbeImageHeader header, bool includeEncryptedDigest)
    {
        var offset = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.Signature);
        offset += 4;
        (header.EncryptedDigest ?? new byte[XbeImageConstants.EncryptedSignatureSize]).AsSpan(0, XbeImageConstants.EncryptedSignatureSize)
            .CopyTo(buffer[offset..]);
        offset += XbeImageConstants.EncryptedSignatureSize;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.BaseAddress);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.SizeOfHeaders);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.SizeOfImage);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.SizeOfImageHeader);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.TimeDateStamp);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.Certificate);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.NumberOfSections);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.SectionHeaders);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.InitFlags);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.AddressOfEntryPoint);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.TlsDirectory);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.SizeOfStackCommit);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.SizeOfHeapReserve);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.SizeOfHeapCommit);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.NtBaseOfDll);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.NtSizeOfImage);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.NtCheckSum);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.NtTimeDateStamp);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.DebugPathName);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.DebugFileName);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.DebugUnicodeFileName);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.XboxKernelThunkData);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.ImportDirectory);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.NumberOfLibraryVersions);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.LibraryVersions);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.XboxKernelLibraryVersion);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.XapiLibraryVersion);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.MicrosoftLogo);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], header.SizeOfMicrosoftLogo);
        _ = includeEncryptedDigest;
    }

    public static byte[] GetCertificateBytes(XbeImageCertificate certificate)
    {
        var buffer = new byte[XbeImageCertificate.Size];
        var offset = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.SizeOfCertificate);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.TimeDateStamp);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.TitleId);
        offset += 4;
        MemoryMarshal.Cast<char, byte>(certificate.TitleName.AsSpan(0, XbeImageConstants.TitleNameLength)).CopyTo(buffer.AsSpan(offset));
        offset += XbeImageConstants.TitleNameLength * 2;
        foreach (var titleId in certificate.AlternateTitleIds)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), titleId);
            offset += 4;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.AllowedMediaTypes);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.GameRegion);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.GameRatings);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.DiskNumber);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.Version);
        offset += 4;
        certificate.LanKey.CopyTo(buffer, offset);
        offset += XbeImageConstants.CertificateKeyLength;
        certificate.SignatureKey.CopyTo(buffer, offset);
        offset += XbeImageConstants.CertificateKeyLength;
        certificate.AlternateSignatureKeysFlat.CopyTo(buffer, offset);
        offset += XbeImageConstants.AlternateTitleIdCount * XbeImageConstants.CertificateKeyLength;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.OriginalSizeOfCertificate);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), certificate.OnlineServiceName);
        return buffer;
    }

    public static byte[] GetImportDescriptorBytes(XbeImageImportDescriptor descriptor)
    {
        var buffer = new byte[Marshal.SizeOf<XbeImageImportDescriptor>()];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), descriptor.ImageThunkData);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), descriptor.ImageName);
        return buffer;
    }

    public static void WriteSectionToBytes(Span<byte> buffer, XbeImageSection section)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, section.SectionFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], section.VirtualAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], section.VirtualSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], section.PointerToRawData);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[16..], section.SizeOfRawData);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[20..], section.SectionName);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[24..], section.SectionReferenceCount);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[28..], section.HeadSharedPageReferenceCount);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[32..], section.TailSharedPageReferenceCount);
        section.SectionDigest.CopyTo(buffer[36..]);
    }

    public static byte[] GetSectionBytes(XbeImageSection section)
    {
        var buffer = new byte[56];
        WriteSectionToBytes(buffer, section);
        return buffer;
    }
}

internal static class StructMarshal
{
    public static byte[] GetBytes<T>(T value) where T : struct
    {
        var buffer = new byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(buffer, in value);
        return buffer;
    }
}
