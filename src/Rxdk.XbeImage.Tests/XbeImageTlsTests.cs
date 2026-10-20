using System.Buffers.Binary;
using Rxdk.XbeImage;
using Xunit;

namespace Rxdk.XbeImage.Tests;

/// <summary>
/// The TLS directory imagebld writes. TriangleXDK.exe (checked in) is an XDK title whose TLS
/// template is all zeros; TriangleXDK.xbe is what the original XDK imagebld made from it.
/// </summary>
public sealed class XbeImageTlsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xbe-tls-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (byte[] Image, XbeImageHeader Header, ImageTlsDirectory32 Tls) Build(string pe)
    {
        var output = Path.Combine(_dir, $"{Guid.NewGuid():N}.xbe");
        XbeImageBuilderTests.BuildManaged(pe, output, 0x5F000000u);
        var image = File.ReadAllBytes(output);
        var header = XbeImageReader.ReadHeader(image);
        Assert.NotEqual(0u, header.TlsDirectory);
        var tls = XbeImageReader.ReadTlsDirectory(image, header.TlsDirectory);
        Assert.NotNull(tls);
        return (image, header, tls.Value);
    }

    [Fact]
    public void All_zero_template_becomes_zero_fill_as_the_xdk_writes_it()
    {
        Assert.True(File.Exists(TestPaths.TriangleXbe), $"missing test input {TestPaths.TriangleXbe}");
        var xdkImage = File.ReadAllBytes(TestPaths.TriangleXbe);
        var xdk = XbeImageReader.ReadTlsDirectory(xdkImage, XbeImageReader.ReadHeader(xdkImage).TlsDirectory)!.Value;

        var built = Build(TestPaths.TriangleExe).Tls;

        // The XDK keeps no template at all: Start = End = 0 and the whole size as zero fill.
        Assert.Equal(0u, xdk.StartAddressOfRawData);
        Assert.Equal(xdk.StartAddressOfRawData, built.StartAddressOfRawData);
        Assert.Equal(xdk.EndAddressOfRawData, built.EndAddressOfRawData);
        Assert.Equal(xdk.SizeOfZeroFill, built.SizeOfZeroFill);
        Assert.Equal(xdk.AddressOfIndex, built.AddressOfIndex);
        Assert.Equal(xdk.AddressOfCallbacks, built.AddressOfCallbacks);
    }

    [Fact]
    public void Non_zero_template_is_kept_inside_the_image()
    {
        // TriangleXDK.exe with its first template bytes made non-zero.
        var pe = File.ReadAllBytes(TestPaths.TriangleExe);
        var templateOffset = TlsTemplateFileOffset(pe, out var templateSize);
        byte[] marker = [0x52, 0x58, 0x44, 0x4B, 0x54, 0x4C, 0x53, 0x21];
        Assert.True(templateSize > marker.Length);
        marker.CopyTo(pe, templateOffset);
        var patched = Path.Combine(_dir, "tls-template.exe");
        File.WriteAllBytes(patched, pe);

        var (image, header, tls) = Build(patched);

        Assert.NotEqual(0u, tls.StartAddressOfRawData);
        // Trailing zeros fold into the zero fill; the marker is what remains of the template.
        Assert.Equal((uint)marker.Length, tls.EndAddressOfRawData - tls.StartAddressOfRawData);
        Assert.Equal((uint)(templateSize - marker.Length), tls.SizeOfZeroFill);
        var imageEnd = header.BaseAddress + header.SizeOfImage;
        Assert.True(tls.StartAddressOfRawData >= header.BaseAddress && tls.EndAddressOfRawData <= imageEnd,
            $"TLS template {tls.StartAddressOfRawData:X8}-{tls.EndAddressOfRawData:X8} is outside the image");
        var templateInImage = XbeImageReader.MapVirtualAddress(image, tls.StartAddressOfRawData, marker.Length);
        Assert.NotNull(templateInImage);
        Assert.Equal(marker, image.AsSpan(templateInImage.Value, marker.Length).ToArray());
    }

    // The file offset and size of a PE's TLS template (StartAddressOfRawData..EndAddressOfRawData).
    private static int TlsTemplateFileOffset(byte[] pe, out int size)
    {
        var ntHeaders = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(0x3C));
        var optional = ntHeaders + 24;
        var imageBase = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(optional + 28));
        var tlsRva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(optional + 96 + 9 * 8));
        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(ntHeaders + 6));
        var sections = optional + BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(ntHeaders + 20));

        int FileOffset(uint rva)
        {
            for (var i = 0; i < sectionCount; i++)
            {
                var s = pe.AsSpan(sections + i * 40);
                var virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
                var virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
                var rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(s[20..]);
                if (rva >= virtualAddress && rva < virtualAddress + virtualSize)
                    return (int)(rawPointer + (rva - virtualAddress));
            }
            throw new InvalidOperationException($"RVA {rva:X8} is in no section");
        }

        var directory = pe.AsSpan(FileOffset(tlsRva));
        var start = BinaryPrimitives.ReadUInt32LittleEndian(directory);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(directory[4..]);
        size = (int)(end - start);
        return FileOffset(start - imageBase);
    }
}
