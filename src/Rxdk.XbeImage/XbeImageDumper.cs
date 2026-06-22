using System.Globalization;
using System.Text;

namespace Rxdk.XbeImage;

public static class XbeImageDumper
{
    private const string DumpPadding = "   ";

    private static readonly string[] ApprovedStatus =
    [
        "unapproved",
        "possibly approved",
        "approved",
        "expired",
    ];

    public static void Dump(string xbePath, TextWriter output)
    {
        byte[] image;
        try
        {
            image = File.ReadAllBytes(xbePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            throw new XbeImageException($"Cannot open input file {xbePath}.");
        }

        output.WriteLine($"Dump of file {xbePath}");
        output.WriteLine();
        DumpImage(image, output);
    }

    internal static void DumpImage(ReadOnlySpan<byte> image, TextWriter output)
    {
        var header = XbeImageReader.ReadHeader(image);

        output.WriteLine("IMAGE HEADER VALUES");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.BaseAddress)} base address");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.SizeOfHeaders)} size of headers");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.SizeOfImage)} size of image");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.SizeOfImageHeader)} size of image header");
        output.Write($"{DumpPadding}{FormatHex8Zero(header.TimeDateStamp)} time date stamp {GetDumpTimeStampString(image, XbeImageReader.BaseAddressOffset + 16)}");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.Certificate)} certificate address");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.NumberOfSections)} number of sections");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.SectionHeaders)} section headers address");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.InitFlags)} initialization flags");

        if ((header.InitFlags & XbeImageConstants.XinitMountUtilityDrive) != 0)
        {
            output.WriteLine($"{DumpPadding}         Mount utility drive");
        }

        if ((header.InitFlags & XbeImageConstants.XinitFormatUtilityDrive) != 0)
        {
            output.WriteLine($"{DumpPadding}         Format utility drive");
        }

        if ((header.InitFlags & XbeImageConstants.XinitLimitDevkitMemory) != 0)
        {
            output.WriteLine($"{DumpPadding}         Limit development kit memory");
        }

        if ((header.InitFlags & XbeImageConstants.XinitNoSetupHardDisk) != 0)
        {
            output.WriteLine($"{DumpPadding}         Don't setup hard disk");
        }

        if ((header.InitFlags & XbeImageConstants.XinitDontModifyHardDisk) != 0)
        {
            output.WriteLine($"{DumpPadding}         Don't modify hard disk");
        }

        var clusterShift = (header.InitFlags & XbeImageConstants.XinitUtilityDriveClusterSizeMask) >>
                           (int)XbeImageConstants.XinitUtilityDriveClusterSizeShift;
        output.WriteLine($"{DumpPadding}         {16 << (int)clusterShift}K utility drive cluster size");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.AddressOfEntryPoint)} entry point address");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.TlsDirectory)} thread local storage directory address");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.SizeOfStackCommit)} size of stack commit");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.SizeOfHeapCommit)} size of heap commit");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.SizeOfHeapReserve)} size of heap reserve");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.NtBaseOfDll)} original base address");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.NtSizeOfImage)} original size of image");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.NtCheckSum)} original checksum");
        output.Write($"{DumpPadding}{FormatHex8Zero(header.NtTimeDateStamp)} original time date stamp {GetDumpTimeStampString(image, XbeImageReader.BaseAddressOffset + 68)}");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.DebugPathName)} debug path name address");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.DebugFileName)} debug file name address");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.DebugUnicodeFileName)} debug Unicode file name address");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.XboxKernelThunkData)} kernel image thunk address");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.ImportDirectory)} non-kernel import directory address");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.NumberOfLibraryVersions)} number of library versions");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.LibraryVersions)} library versions address");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.XboxKernelLibraryVersion)} kernel library version address");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.XapiLibraryVersion)} XAPI library version address");
        output.WriteLine($"{DumpPadding}{FormatAddress(header.MicrosoftLogo)} logo bitmap address");
        output.WriteLine($"{DumpPadding}{FormatHex8(header.SizeOfMicrosoftLogo)} logo bitmap size");
        output.WriteLine();

        if (header.DebugPathName != 0)
        {
            var debugPath = XbeImageReader.ReadAnsiStringAtVirtualAddress(image, header.DebugPathName);
            output.WriteLine($"Debug path name: {debugPath}");
            output.WriteLine();
        }

        DumpCertificate(image, header, output);
        DumpLibraryVersions(image, header, output);
        DumpImportDirectories(image, header, output);
        DumpTlsDirectory(image, header, output);
        DumpSectionHeaders(image, header, output);
    }

    private static void DumpCertificate(ReadOnlySpan<byte> image, XbeImageHeader header, TextWriter output)
    {
        if (header.Certificate == 0)
        {
            return;
        }

        var certificate = XbeImageReader.ReadCertificate(image, header.Certificate);
        if (certificate is null)
        {
            return;
        }

        var certificateOffset = XbeImageReader.MapVirtualAddress(image, header.Certificate, 4);
        if (certificateOffset is null)
        {
            return;
        }

        output.WriteLine("CERTIFICATE");
        output.WriteLine($"{DumpPadding}{FormatHex8(certificate.Value.SizeOfCertificate)} size of certificate");
        output.Write($"{DumpPadding}{FormatHex8Zero(certificate.Value.TimeDateStamp)} time date stamp {GetDumpTimeStampString(image, certificateOffset.Value + 4)}");
        output.WriteLine($"{DumpPadding}{FormatHex8Zero(certificate.Value.TitleId)} title id");
        output.WriteLine($"{DumpPadding}{FormatHex8(certificate.Value.AllowedMediaTypes)} allowed media types");
        output.WriteLine($"{DumpPadding}{FormatHex8(certificate.Value.GameRegion)} game region");
        output.WriteLine($"{DumpPadding}{FormatHex8(certificate.Value.GameRatings)} game ratings");
        output.WriteLine($"{DumpPadding}{FormatHex8(certificate.Value.DiskNumber)} disk number");
        output.WriteLine($"{DumpPadding}{FormatHex8(certificate.Value.Version)} version");

        if (certificate.Value.SizeOfCertificate > XbeImageConstants.CertificateOnlineServiceNameOffset)
        {
            output.WriteLine($"{DumpPadding}{FormatHex8(certificate.Value.OnlineServiceName)} online service name");
        }

        output.WriteLine();
        output.WriteLine($"   Title name: {XbeImageReader.ReadCertificateTitleName(certificate.Value)}");
        output.WriteLine();
        output.WriteLine($"      LAN key: {FormatCertificateKey(certificate.Value.LanKey)}");
        output.WriteLine($"Signature key: {FormatCertificateKey(certificate.Value.SignatureKey)}");
        output.WriteLine();

        if (certificate.Value.AlternateTitleIds[0] != 0)
        {
            output.WriteLine("Title alternate title ids:");

            for (var index = 0; index < XbeImageConstants.AlternateTitleIdCount; index++)
            {
                if (certificate.Value.AlternateTitleIds[index] == 0)
                {
                    break;
                }

                output.WriteLine(
                    $"{DumpPadding}{certificate.Value.AlternateTitleIds[index]:x8}    Signature key: {FormatCertificateKey(XbeImageReader.ReadCertificateKey(certificate.Value.AlternateSignatureKeysFlat, index))}");
            }

            output.WriteLine();
        }
    }

    private static void DumpLibraryVersions(ReadOnlySpan<byte> image, XbeImageHeader header, TextWriter output)
    {
        if (header.NumberOfLibraryVersions == 0)
        {
            return;
        }

        var libraryVersions = XbeImageReader.ReadLibraryVersions(image, header.LibraryVersions, header.NumberOfLibraryVersions);
        if (libraryVersions.Length == 0)
        {
            return;
        }

        XbeImageLibraryVersion? xapiLibraryVersion = null;
        if (header.XapiLibraryVersion != 0)
        {
            var xapiVersions = XbeImageReader.ReadLibraryVersions(image, header.XapiLibraryVersion, 1);
            if (xapiVersions.Length > 0)
            {
                xapiLibraryVersion = xapiVersions[0];
            }
        }

        LibraryVersionChecker.CheckLibraryApprovalStatus(
            xapiLibraryVersion,
            libraryVersions,
            (library, approvalLevel) =>
            {
                for (var i = 0; i < libraryVersions.Length; i++)
                {
                    if (libraryVersions[i].LibraryName.AsSpan()
                        .SequenceEqual(library.LibraryName.AsSpan(0, XbeImageConstants.LibraryVersionNameLength)))
                    {
                        libraryVersions[i].SetApprovedLibrary(approvalLevel);
                        break;
                    }
                }
            });

        output.WriteLine("LIBRARY VERSIONS");

        foreach (var libraryVersion in libraryVersions)
        {
            var approvalStatus = libraryVersion.ApprovedLibrary;
            var libraryName = Encoding.ASCII.GetString(libraryVersion.LibraryName).TrimEnd('\0');
            var debugSuffix = libraryVersion.DebugBuild ? " [debug]" : string.Empty;
            output.WriteLine(
                $"{DumpPadding}{libraryName,8} {libraryVersion.MajorVersion}.{libraryVersion.MinorVersion}.{libraryVersion.BuildVersion}.{libraryVersion.QfeVersion}{debugSuffix} [{ApprovedStatus[approvalStatus]}]");
        }

        output.WriteLine();
    }

    private static void DumpImportDirectories(ReadOnlySpan<byte> image, XbeImageHeader header, TextWriter output)
    {
        if (header.ImportDirectory == 0)
        {
            return;
        }

        var moduleNames = XbeImageReader.ReadImportModuleNames(image, header.ImportDirectory);
        if (moduleNames.Count == 0)
        {
            return;
        }

        output.WriteLine("NON-KERNEL IMPORT MODULES");
        foreach (var moduleName in moduleNames)
        {
            output.WriteLine($"{DumpPadding}{moduleName}");
        }

        output.WriteLine();
    }

    private static void DumpTlsDirectory(ReadOnlySpan<byte> image, XbeImageHeader header, TextWriter output)
    {
        if (header.TlsDirectory == 0)
        {
            return;
        }

        var tlsDirectory = XbeImageReader.ReadTlsDirectory(image, header.TlsDirectory);
        if (tlsDirectory is null)
        {
            return;
        }

        output.WriteLine("THREAD LOCAL STORAGE DIRECTORY");
        output.WriteLine($"{DumpPadding}{FormatHex8Zero(tlsDirectory.Value.StartAddressOfRawData)} raw data start address");
        output.WriteLine($"{DumpPadding}{FormatHex8Zero(tlsDirectory.Value.EndAddressOfRawData)} raw data end address");
        output.WriteLine($"{DumpPadding}{FormatHex8Zero(tlsDirectory.Value.AddressOfIndex)} TLS index address");
        output.WriteLine($"{DumpPadding}{FormatHex8Zero(tlsDirectory.Value.AddressOfCallbacks)} TLS callbacks address");
        output.WriteLine($"{DumpPadding}{FormatHex8(tlsDirectory.Value.SizeOfZeroFill)} size of zero fill");
        output.WriteLine($"{DumpPadding}{FormatHex8(tlsDirectory.Value.Characteristics)} characteristics");
        output.WriteLine();
    }

    private static void DumpSectionHeaders(ReadOnlySpan<byte> image, XbeImageHeader header, TextWriter output)
    {
        if (header.NumberOfSections == 0)
        {
            return;
        }

        var sections = XbeImageReader.ReadSections(image, header.SectionHeaders, header.NumberOfSections);
        if (sections.Length == 0)
        {
            return;
        }

        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            var sectionName = XbeImageReader.ReadSectionName(image, header.BaseAddress, section.SectionName);

            output.WriteLine($"SECTION HEADER #{index + 1}  {sectionName}");
            output.WriteLine($"{DumpPadding}{FormatHex8(section.VirtualAddress)} virtual address");
            output.WriteLine($"{DumpPadding}{FormatHex8(section.VirtualSize)} virtual size");
            output.WriteLine($"{DumpPadding}{FormatHex8(section.PointerToRawData)} file pointer to raw data");
            output.WriteLine($"{DumpPadding}{FormatHex8(section.SizeOfRawData)} size of raw data");
            output.WriteLine($"{DumpPadding}{FormatAddress(section.HeadSharedPageReferenceCount)} head shared page reference count address");
            output.WriteLine($"{DumpPadding}{FormatAddress(section.TailSharedPageReferenceCount)} tail shared page reference count address");
            output.WriteLine($"{DumpPadding}{FormatHex8(section.SectionFlags)} flags");

            if ((section.SectionFlags & XbeImageConstants.SectionWriteable) != 0)
            {
                output.WriteLine($"{DumpPadding}         Writeable");
            }

            if ((section.SectionFlags & XbeImageConstants.SectionPreload) != 0)
            {
                output.WriteLine($"{DumpPadding}         Preload");
            }

            if ((section.SectionFlags & XbeImageConstants.SectionExecutable) != 0)
            {
                output.WriteLine($"{DumpPadding}         Executable");
            }

            if ((section.SectionFlags & XbeImageConstants.SectionInsertFile) != 0)
            {
                output.WriteLine($"{DumpPadding}         Inserted file");
            }

            if ((section.SectionFlags & XbeImageConstants.SectionHeadPageReadonly) != 0)
            {
                output.WriteLine($"{DumpPadding}         Head page read-only");
            }

            if ((section.SectionFlags & XbeImageConstants.SectionTailPageReadonly) != 0)
            {
                output.WriteLine($"{DumpPadding}         Tail page read-only");
            }

            output.WriteLine();
        }
    }

    private static string GetDumpTimeStampString(ReadOnlySpan<byte> image, int timeDateStampOffset)
    {
        if (timeDateStampOffset + 8 > image.Length)
        {
            return "invalid\n";
        }

        var timeValue = BitConverter.ToInt64(image.Slice(timeDateStampOffset, 8));
        if (timeValue < 0)
        {
            return "invalid\n";
        }

        try
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(timeValue).LocalDateTime;
            return timestamp.ToString("ddd MMM dd HH:mm:ss yyyy\n", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "invalid\n";
        }
    }

    private static string FormatCertificateKey(byte[] certificateKey)
    {
        var builder = new StringBuilder(certificateKey.Length * 2);
        foreach (var value in certificateKey)
        {
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string FormatHex8(uint value) =>
        value.ToString("X", CultureInfo.InvariantCulture).PadLeft(8);

    private static string FormatHex8Zero(uint value) =>
        value.ToString("X8", CultureInfo.InvariantCulture);

    private static string FormatAddress(uint address) =>
        address.ToString("X8", CultureInfo.InvariantCulture);
}
