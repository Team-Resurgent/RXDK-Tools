using System.Buffers.Binary;

namespace Rxdk.Xbdm.Tests;

internal static class BmpTestHelper
{
    internal readonly record struct Info(int Width, int Height, ushort BitCount, int PixelDataSize);

    internal static Info ReadInfo(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> fileHeader = stackalloc byte[14];
        stream.ReadExactly(fileHeader);
        if (BinaryPrimitives.ReadUInt16LittleEndian(fileHeader) != 0x4D42)
            throw new InvalidDataException("Not a BMP file.");

        var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(fileHeader[10..]);
        Span<byte> infoHeader = stackalloc byte[40];
        stream.ReadExactly(infoHeader);

        var width = BinaryPrimitives.ReadInt32LittleEndian(infoHeader[4..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(infoHeader[8..]);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(infoHeader[14..]);
        var pixelDataSize = (int)(stream.Length - pixelOffset);

        return new Info(width, Math.Abs(height), bitCount, pixelDataSize);
    }
}
