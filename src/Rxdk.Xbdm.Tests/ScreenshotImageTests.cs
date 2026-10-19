using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.Versioning;
using Rxdk.Xbdm.Managed;
using Xunit;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Screenshot encoders (xbcapture, Xbox Neighborhood, the shell extension). PNG and BMP must
/// round-trip exactly; JPEG is checked with Windows' own decoder (GDI+), so these tests run on
/// Windows, as CI does.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenshotImageTests
{
    /// <summary>A frame with smooth gradients, hard edges and a flat area, like a rendered scene.</summary>
    private static byte[] TestFrame(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                if (x < width / 3)
                {
                    rgb[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                    rgb[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                    rgb[i + 2] = 128;
                }
                else if (x < 2 * width / 3)
                {
                    var on = ((x / 4) + (y / 4)) % 2 == 0;
                    rgb[i] = on ? (byte)230 : (byte)20;
                    rgb[i + 1] = on ? (byte)40 : (byte)200;
                    rgb[i + 2] = on ? (byte)40 : (byte)60;
                }
                else
                {
                    rgb[i] = 30;
                    rgb[i + 1] = 90;
                    rgb[i + 2] = 200;
                }
            }
        }
        return rgb;
    }

    [Theory]
    [InlineData("shot.png", ScreenshotImageFormat.Png)]
    [InlineData("SHOT.PNG", ScreenshotImageFormat.Png)]
    [InlineData("shot.jpg", ScreenshotImageFormat.Jpeg)]
    [InlineData("shot.jpeg", ScreenshotImageFormat.Jpeg)]
    [InlineData(@"C:\shots\shot.JPG", ScreenshotImageFormat.Jpeg)]
    [InlineData("shot.bmp", ScreenshotImageFormat.Bmp)]
    public void FormatFromPath_FollowsTheExtension(string path, ScreenshotImageFormat expected)
    {
        Assert.Equal(expected, ScreenshotImage.FormatFromPath(path));
    }

    [Theory]
    [InlineData("shot.gif")]
    [InlineData("shot")]
    [InlineData("shot.png.txt")]
    public void FormatFromPath_RejectsOtherTypes(string path)
    {
        var ex = Assert.Throws<ArgumentException>(() => ScreenshotImage.FormatFromPath(path));
        Assert.Contains(".png", ex.Message);
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(13, 9)]
    [InlineData(1, 1)]
    public void Png_RoundTripsExactly(int width, int height)
    {
        var rgb = TestFrame(width, height);
        Assert.Equal(rgb, DecodePng(ScreenshotImage.EncodePng(width, height, rgb), out var w, out var h));
        Assert.Equal((width, height), (w, h));
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(13, 9)]
    public void Png_DecodesWithWindows(int width, int height)
    {
        var rgb = TestFrame(width, height);
        Assert.Equal(rgb, DecodeWithGdi(ScreenshotImage.EncodePng(width, height, rgb), width, height));
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(3, 2)] // 9-byte rows pad to 12
    [InlineData(13, 9)]
    public void Bmp_RoundTripsExactly_WithPaddedRows(int width, int height)
    {
        var rgb = TestFrame(width, height);
        var bmp = ScreenshotImage.EncodeBmp(width, height, rgb);
        var stride = (width * 3 + 3) & ~3;
        Assert.Equal(54 + stride * height, bmp.Length);
        Assert.Equal(rgb, DecodeWithGdi(bmp, width, height));
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(13, 9)] // partial edge blocks
    [InlineData(1, 1)]
    public void Jpeg_DecodesWithWindows_CloseToTheSource(int width, int height)
    {
        var rgb = TestFrame(width, height);
        var jpeg = ScreenshotImage.EncodeJpeg(width, height, rgb);
        var decoded = DecodeWithGdi(jpeg, width, height);
        Assert.True(Psnr(rgb, decoded) > 30, $"PSNR {Psnr(rgb, decoded):F1} dB");
    }

    [Fact]
    public void Jpeg_FlatColour_StaysThatColour()
    {
        const int width = 64, height = 48;
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = 200;
            rgb[i + 1] = 100;
            rgb[i + 2] = 50;
        }
        var decoded = DecodeWithGdi(ScreenshotImage.EncodeJpeg(width, height, rgb), width, height);
        for (var i = 0; i < rgb.Length; i++)
            Assert.InRange(decoded[i] - rgb[i], -3, 3);
    }

    [Fact]
    public void Jpeg_QualityTradesSizeForFidelity()
    {
        var rgb = TestFrame(320, 240);
        var low = ScreenshotImage.EncodeJpeg(320, 240, rgb, 30);
        var high = ScreenshotImage.EncodeJpeg(320, 240, rgb, 95);
        Assert.True(low.Length < high.Length);
        Assert.True(Psnr(rgb, DecodeWithGdi(high, 320, 240)) > Psnr(rgb, DecodeWithGdi(low, 320, 240)));
    }

    [Fact]
    public void Jpeg_HasTheBaselineMarkers()
    {
        var jpeg = ScreenshotImage.EncodeJpeg(16, 16, TestFrame(16, 16));
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpeg[..2]);   // SOI
        Assert.Equal(new byte[] { 0xFF, 0xD9 }, jpeg[^2..]);  // EOI
        Assert.True(IndexOf(jpeg, new byte[] { 0xFF, 0xC0 }) > 0); // SOF0 (baseline)
    }

    [Fact]
    public void ToRgb_Expands16BitChannelsToFullRange()
    {
        // LIN_R5G6B5: white, pure red, pure green, pure blue.
        var info = XbdmScreenshot.ParseInfoLine("pitch=0x8 height=0x1 width=0x4 format=0x11 framebuffersize=0x8");
        var fb = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(fb, 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(fb.AsSpan(2), 0xF800);
        BinaryPrimitives.WriteUInt16LittleEndian(fb.AsSpan(4), 0x07E0);
        BinaryPrimitives.WriteUInt16LittleEndian(fb.AsSpan(6), 0x001F);
        Assert.Equal(new byte[] { 255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255 }, XbdmScreenshot.ToRgb(info, fb));
    }

    [Fact]
    public void ToRgb_X1R5G5B5_ExpandsEachChannel()
    {
        var info = XbdmScreenshot.ParseInfoLine("pitch=0x2 height=0x1 width=0x1 format=0x1c framebuffersize=0x2");
        var fb = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(fb, 0x7C1F); // red 31, green 0, blue 31
        Assert.Equal(new byte[] { 255, 0, 255 }, XbdmScreenshot.ToRgb(info, fb));
    }

    [Fact]
    public void ToRgb_32Bit_HonoursThePitch()
    {
        // Two pixels per row in a 12-byte pitch (4 bytes of row padding).
        var info = XbdmScreenshot.ParseInfoLine("pitch=0xc height=0x2 width=0x2 format=0x1e framebuffersize=0x18");
        var fb = new byte[24];
        byte[] row0 = { 1, 2, 3, 0, 4, 5, 6, 0, 0xEE, 0xEE, 0xEE, 0xEE };
        byte[] row1 = { 7, 8, 9, 0, 10, 11, 12, 0, 0xEE, 0xEE, 0xEE, 0xEE };
        row0.CopyTo(fb, 0);
        row1.CopyTo(fb, 12);
        Assert.Equal(new byte[] { 3, 2, 1, 6, 5, 4, 9, 8, 7, 12, 11, 10 }, XbdmScreenshot.ToRgb(info, fb));
    }

    // ---- helpers

    private static byte[] DecodePng(byte[] png, out int width, out int height)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        width = height = 0;
        using var idat = new MemoryStream();
        var pos = 8;
        while (pos < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                Assert.Equal(new byte[] { 8, 2, 0, 0, 0 }, data[8..].ToArray());
            }
            else if (type == "IDAT")
                idat.Write(data);
            pos += 12 + length;
        }

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var filtered = raw.ToArray();
        var rowBytes = width * 3;
        var rgb = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[y * (rowBytes + 1)];
            Assert.Equal(1, filter); // Sub
            for (var i = 0; i < rowBytes; i++)
            {
                var left = i >= 3 ? rgb[y * rowBytes + i - 3] : 0;
                rgb[y * rowBytes + i] = (byte)(filtered[y * (rowBytes + 1) + 1 + i] + left);
            }
        }
        return rgb;
    }

    private static byte[] DecodeWithGdi(byte[] encoded, int width, int height)
    {
        using var stream = new MemoryStream(encoded);
        using var bitmap = new System.Drawing.Bitmap(stream);
        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var i = (y * width + x) * 3;
                rgb[i] = c.R;
                rgb[i + 1] = c.G;
                rgb[i + 2] = c.B;
            }
        }
        return rgb;
    }

    private static double Psnr(byte[] a, byte[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }
        var mse = sum / a.Length;
        return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
    }

    private static int IndexOf(byte[] data, byte[] pattern)
    {
        for (var i = 0; i + pattern.Length <= data.Length; i++)
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                return i;
        return -1;
    }
}
