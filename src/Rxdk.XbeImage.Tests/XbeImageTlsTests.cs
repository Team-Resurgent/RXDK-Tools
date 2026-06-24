using Rxdk.XbeImage;
using Xunit;

namespace Rxdk.XbeImage.Tests;

public sealed class XbeImageTlsTests
{
    private static readonly string XapiSmokePe = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "RXDK-LibsZig", "zig-out", "samples", "xapi-smoke", "xapi-smoke.exe"));

    [Fact]
    public void Build_tls_directory_start_is_mapped_when_tls_section_is_retained()
    {
        var pe = File.Exists(XapiSmokePe) ? XapiSmokePe : TestPaths.TriangleExe;
        if (!File.Exists(pe))
        {
            return;
        }

        var output = Path.Combine(Path.GetTempPath(), $"tls-{Guid.NewGuid():N}.xbe");
        try
        {
            XbeImageBuilderTests.BuildManaged(pe, output, 0x5F000000u);
            var image = File.ReadAllBytes(output);
            var header = XbeImageReader.ReadHeader(image);
            if (header.TlsDirectory == 0)
            {
                return;
            }

            var tls = XbeImageReader.ReadTlsDirectory(image, header.TlsDirectory);
            Assert.NotNull(tls);
            if (tls.Value.StartAddressOfRawData == 0)
            {
                return;
            }

            var imageEnd = header.BaseAddress + header.SizeOfImage;
            Assert.True(
                tls.Value.StartAddressOfRawData < imageEnd,
                $"TLS template start {tls.Value.StartAddressOfRawData:X8} is outside SizeOfImage {header.SizeOfImage:X8}");
            Assert.True(
                tls.Value.EndAddressOfRawData <= imageEnd,
                $"TLS template end {tls.Value.EndAddressOfRawData:X8} is outside SizeOfImage {header.SizeOfImage:X8}");
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }
}
