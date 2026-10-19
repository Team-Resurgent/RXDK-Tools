using System.Buffers.Binary;
using System.Numerics;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

internal static class XbdmScreenshot
{
    internal readonly record struct Info(
        uint Pitch,
        uint Height,
        uint Width,
        uint Format,
        uint FrameBufferSize,
        int BitCount,
        int SrcWidth,
        ushort RedMask,
        ushort GreenMask,
        ushort BlueMask,
        int RedShift,
        int GreenShift,
        int BlueUpShift);

    internal static Info ParseInfoLine(string line)
    {
        var pitch = XbdmProtocol.GetDwParam(line, "pitch");
        var height = XbdmProtocol.GetDwParam(line, "height");
        var width = XbdmProtocol.GetDwParam(line, "width");
        var format = XbdmProtocol.GetDwParam(line, "format");
        var frameBufferSize = XbdmProtocol.GetDwParam(line, "framebuffersize");

        if (pitch == 0 || height == 0 || width == 0 || format == 0 || frameBufferSize == 0)
            throw XbdmException.FromHResult("Screenshot metadata was incomplete.", XbdmHResults.FileError, line);

        return format switch
        {
            0x00000012 or 0x0000001E => new Info(
                pitch, height, width, format, frameBufferSize,
                32, (int)(width * 4), 0, 0, 0, 0, 0, 0),
            0x00000011 => new Info(
                pitch, height, width, format, frameBufferSize,
                16, (int)(width * 2), 0xF800, 0x07E0, 0x001F, 8, 3, 3),
            0x0000001C => new Info(
                pitch, height, width, format, frameBufferSize,
                16, (int)(width * 2), 0x7C00, 0x03E0, 0x001F, 7, 2, 3),
            _ => throw XbdmException.FromHResult(
                $"Unsupported screenshot format 0x{format:x8}.", XbdmHResults.FileError, line),
        };
    }

    internal static byte[] ReadFramebuffer(XbdmProtocolSession session)
    {
        var (hr, line) = session.SendCommandRaw("screenshot");
        if (hr != XbdmHResults.Binresponse)
            throw XbdmException.FromHResult("screenshot failed.", hr, line);

        var infoLine = session.ReceiveLine();
        var info = ParseInfoLine(infoLine);
        var buffer = new byte[info.FrameBufferSize];
        session.BeginPendingBinary(info.FrameBufferSize);
        session.ReceiveBinary(buffer);
        return buffer;
    }

    /// <summary>
    /// Receive the frame that follows the screenshot info line and save it; the path's extension
    /// picks the format (see <see cref="ScreenshotImage.FormatFromPath"/>).
    /// </summary>
    internal static void WriteImage(string localPath, Info info, XbdmProtocolSession session)
    {
        var format = ScreenshotImage.FormatFromPath(localPath);
        var frameBuffer = new byte[info.FrameBufferSize];
        session.BeginPendingBinary(info.FrameBufferSize);
        session.ReceiveBinary(frameBuffer);
        WriteImageFromFramebuffer(localPath, info, frameBuffer, format);
    }

    internal static void WriteImageFromFramebuffer(string localPath, Info info, ReadOnlySpan<byte> frameBuffer) =>
        WriteImageFromFramebuffer(localPath, info, frameBuffer, ScreenshotImage.FormatFromPath(localPath));

    private static void WriteImageFromFramebuffer(
        string localPath, Info info, ReadOnlySpan<byte> frameBuffer, ScreenshotImageFormat format)
    {
        var rgb = ToRgb(info, frameBuffer);
        var encoded = ScreenshotImage.Encode(format, (int)info.Width, (int)info.Height, rgb);

        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(localPath, encoded);
        }
        catch
        {
            if (File.Exists(localPath))
                File.Delete(localPath);
            throw;
        }
    }

    /// <summary>The frame as 24-bit RGB, top row first, with 5/6-bit channels expanded to 0..255.</summary>
    internal static byte[] ToRgb(Info info, ReadOnlySpan<byte> frameBuffer)
    {
        if (frameBuffer.Length < info.FrameBufferSize || (long)info.Pitch * info.Height > frameBuffer.Length)
            throw XbdmException.FromHResult("Screenshot framebuffer was truncated.", XbdmHResults.FileError);

        var width = (int)info.Width;
        var height = (int)info.Height;
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var row = frameBuffer.Slice(y * (int)info.Pitch);
            var dst = rgb.AsSpan(y * width * 3, width * 3);
            if (info.BitCount == 32)
            {
                // X8R8G8B8 / A8R8G8B8: B, G, R, X in memory.
                for (var x = 0; x < width; x++)
                {
                    dst[x * 3] = row[x * 4 + 2];
                    dst[x * 3 + 1] = row[x * 4 + 1];
                    dst[x * 3 + 2] = row[x * 4];
                }
            }
            else
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]);
                    dst[x * 3] = Expand(pixel, info.RedMask);
                    dst[x * 3 + 1] = Expand(pixel, info.GreenMask);
                    dst[x * 3 + 2] = Expand(pixel, info.BlueMask);
                }
            }
        }
        return rgb;
    }

    // A masked channel scaled to 8 bits by repeating its high bits (31 -> 255, not 248).
    private static byte Expand(ushort pixel, ushort mask)
    {
        var shift = BitOperations.TrailingZeroCount(mask);
        var bits = BitOperations.PopCount(mask);
        var value = (pixel & mask) >> shift;
        var expanded = value << (8 - bits);
        return (byte)(expanded | (expanded >> bits));
    }
}
