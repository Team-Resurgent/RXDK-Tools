using System.Buffers.Binary;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

internal static class XbdmScreenshot
{
    private const ushort BmpType = 0x4D42;

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

    internal static void WriteBmp(string localBmpPath, Info info, XbdmProtocolSession session)
    {
        var frameBuffer = new byte[info.FrameBufferSize];
        session.ReceiveBinary(frameBuffer);
        WriteBmpFromFramebuffer(localBmpPath, info, frameBuffer);
    }

    internal static void WriteBmpFromFramebuffer(string localBmpPath, Info info, ReadOnlySpan<byte> frameBuffer)
    {
        if (frameBuffer.Length < info.FrameBufferSize)
            throw XbdmException.FromHResult("Screenshot framebuffer was truncated.", XbdmHResults.FileError);

        var destPitch = (int)(info.Width * 3);
        var pixelData = new byte[destPitch * info.Height];
        ConvertFrameBuffer(frameBuffer, info, pixelData, destPitch);

        var directory = Path.GetDirectoryName(localBmpPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        try
        {
            using var stream = File.Create(localBmpPath);
            WriteBmpHeaders(stream, info.Width, info.Height, destPitch, pixelData);
        }
        catch
        {
            if (File.Exists(localBmpPath))
                File.Delete(localBmpPath);
            throw;
        }
    }

    private static void ConvertFrameBuffer(ReadOnlySpan<byte> source, Info info, Span<byte> destination, int destPitch)
    {
        var height = (int)info.Height;
        var pitch = (int)info.Pitch;
        var srcWidth = info.SrcWidth;
        var offset = 0;

        for (var y = 0; y < height; y++)
        {
            var rowStart = offset;
            var destRow = (height - 1 - y) * destPitch;

            if (info.BitCount == 32)
                ConvertRow32(source, rowStart, srcWidth, destination, destRow);
            else
                ConvertRow16(source, rowStart, srcWidth, destination, destRow, info);

            offset += pitch;
        }
    }

    private static void ConvertRow32(
        ReadOnlySpan<byte> source, int rowStart, int srcWidth, Span<byte> destination, int destRow)
    {
        var pixels = srcWidth / 4;
        var src = rowStart;
        var dst = destRow;
        for (var i = 0; i < pixels; i++)
        {
            destination[dst++] = source[src++];
            destination[dst++] = source[src++];
            destination[dst++] = source[src++];
            src++;
        }
    }

    private static void ConvertRow16(
        ReadOnlySpan<byte> source, int rowStart, int srcWidth, Span<byte> destination, int destRow, Info info)
    {
        var pixels = srcWidth / 2;
        var src = rowStart;
        var dst = destRow;
        for (var i = 0; i < pixels; i++)
        {
            var pixel = BinaryPrimitives.ReadUInt16LittleEndian(source[src..]);
            src += 2;
            destination[dst++] = (byte)((pixel & info.BlueMask) << info.BlueUpShift);
            destination[dst++] = (byte)((pixel & info.GreenMask) >> info.GreenShift);
            destination[dst++] = (byte)((pixel & info.RedMask) >> info.RedShift);
        }
    }

    private static void WriteBmpHeaders(Stream stream, uint width, uint height, int destPitch, ReadOnlySpan<byte> pixelData)
    {
        var fileHeaderSize = 14;
        var infoHeaderSize = 40;
        var pixelOffset = (uint)(fileHeaderSize + infoHeaderSize);
        var fileSize = pixelOffset + (uint)pixelData.Length;

        Span<byte> fileHeader = stackalloc byte[fileHeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(fileHeader, BmpType);
        BinaryPrimitives.WriteUInt32LittleEndian(fileHeader[2..], fileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(fileHeader[10..], pixelOffset);
        stream.Write(fileHeader);

        Span<byte> infoHeader = stackalloc byte[infoHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(infoHeader, (uint)infoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[4..], (int)width);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[8..], (int)height);
        BinaryPrimitives.WriteUInt16LittleEndian(infoHeader[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(infoHeader[14..], 24);
        stream.Write(infoHeader);
        stream.Write(pixelData);
    }
}
