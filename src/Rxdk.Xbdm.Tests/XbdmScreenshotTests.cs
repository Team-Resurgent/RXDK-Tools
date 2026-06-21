using Rxdk.Xbdm.Managed;
using Xunit;

namespace Rxdk.Xbdm.Tests;

public sealed class XbdmScreenshotTests
{
    [Fact]
    public void ParseInfoLine_accepts_lin_x8r8g8b8()
    {
        var info = XbdmScreenshot.ParseInfoLine(
            "pitch=0x1000 height=0x1e0 width=0x280 format=0x1e framebuffersize=0x78000");

        Assert.Equal(0x1000u, info.Pitch);
        Assert.Equal(0x1E0u, info.Height);
        Assert.Equal(0x280u, info.Width);
        Assert.Equal(32, info.BitCount);
        Assert.Equal(0x280 * 4, info.SrcWidth);
    }

    [Fact]
    public void WriteBmpFromFramebuffer_converts_32bpp_to_24bpp_bottom_up()
    {
        const uint width = 2;
        const uint height = 2;
        const uint pitch = 8;
        var info = new XbdmScreenshot.Info(
            pitch, height, width, 0x1E, pitch * height,
            32, (int)(width * 4), 0, 0, 0, 0, 0, 0);

        var frameBuffer = new byte[pitch * height];
        frameBuffer[0] = 10; frameBuffer[1] = 20; frameBuffer[2] = 30; frameBuffer[3] = 255;
        frameBuffer[4] = 40; frameBuffer[5] = 50; frameBuffer[6] = 60; frameBuffer[7] = 255;
        frameBuffer[8] = 1; frameBuffer[9] = 2; frameBuffer[10] = 3; frameBuffer[11] = 255;
        frameBuffer[12] = 4; frameBuffer[13] = 5; frameBuffer[14] = 6; frameBuffer[15] = 255;

        var path = Path.Combine(Path.GetTempPath(), $"xbdm-shot-{Guid.NewGuid():N}.bmp");
        try
        {
            XbdmScreenshot.WriteBmpFromFramebuffer(path, info, frameBuffer);

            var bmp = BmpTestHelper.ReadInfo(path);
            Assert.Equal(2, bmp.Width);
            Assert.Equal(2, bmp.Height);
            Assert.Equal((ushort)24, bmp.BitCount);
            Assert.Equal(12, bmp.PixelDataSize);

            var pixels = File.ReadAllBytes(path)[54..];
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 10, 20, 30, 40, 50, 60 }, pixels);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
