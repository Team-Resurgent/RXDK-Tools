using System.Buffers.Binary;
using System.IO.Compression;

namespace Rxdk.Xbdm.Managed;

/// <summary>Image file types a screenshot can be saved as.</summary>
public enum ScreenshotImageFormat
{
    Png,
    Jpeg,
    Bmp,
}

/// <summary>
/// Encodes a captured frame (24-bit RGB, top row first) as PNG, JPEG or BMP. Self-contained so
/// every host tool (xbcapture, Xbox Neighborhood, the shell extension) and every OS writes the
/// same files with no imaging dependency.
/// </summary>
public static class ScreenshotImage
{
    /// <summary>Extensions accepted for a screenshot path, in the order tools should offer them.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".png", ".jpg", ".jpeg", ".bmp" };

    /// <summary>JPEG quality (1-100) used for screenshots.</summary>
    public const int DefaultJpegQuality = 90;

    /// <summary>The format a path's extension asks for; throws for any other extension.</summary>
    public static ScreenshotImageFormat FormatFromPath(string path)
    {
        if (TryGetFormatFromPath(path, out var format))
            return format;
        var ext = Path.GetExtension(path);
        throw new ArgumentException(
            $"Unsupported screenshot file type '{(ext.Length == 0 ? "(none)" : ext)}': use {string.Join(", ", SupportedExtensions)}.",
            nameof(path));
    }

    public static bool TryGetFormatFromPath(string path, out ScreenshotImageFormat format)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".png": format = ScreenshotImageFormat.Png; return true;
            case ".jpg" or ".jpeg": format = ScreenshotImageFormat.Jpeg; return true;
            case ".bmp": format = ScreenshotImageFormat.Bmp; return true;
            default: format = default; return false;
        }
    }

    /// <summary>Encode <paramref name="rgb"/> (width*height*3 bytes, R,G,B, top row first).</summary>
    public static byte[] Encode(ScreenshotImageFormat format, int width, int height, ReadOnlySpan<byte> rgb) => format switch
    {
        ScreenshotImageFormat.Png => EncodePng(width, height, rgb),
        ScreenshotImageFormat.Jpeg => EncodeJpeg(width, height, rgb, DefaultJpegQuality),
        ScreenshotImageFormat.Bmp => EncodeBmp(width, height, rgb),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static void CheckImage(int width, int height, ReadOnlySpan<byte> rgb)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Image dimensions must be positive.");
        if (rgb.Length < (long)width * height * 3)
            throw new ArgumentException("Pixel data is shorter than width*height*3.", nameof(rgb));
    }

    // ---------------------------------------------------------------- BMP

    /// <summary>24-bit BI_RGB bitmap: rows bottom-up, B,G,R, each padded to a multiple of 4 bytes.</summary>
    public static byte[] EncodeBmp(int width, int height, ReadOnlySpan<byte> rgb)
    {
        CheckImage(width, height, rgb);
        const int fileHeaderSize = 14, infoHeaderSize = 40;
        var stride = (width * 3 + 3) & ~3;
        var pixelOffset = fileHeaderSize + infoHeaderSize;
        var output = new byte[pixelOffset + stride * height];
        var span = output.AsSpan();

        BinaryPrimitives.WriteUInt16LittleEndian(span, 0x4D42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[2..], (uint)output.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[10..], (uint)pixelOffset);
        var info = span[fileHeaderSize..];
        BinaryPrimitives.WriteUInt32LittleEndian(info, infoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(info[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(info[8..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(info[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(info[14..], 24);
        BinaryPrimitives.WriteUInt32LittleEndian(info[20..], (uint)(stride * height));

        for (var y = 0; y < height; y++)
        {
            var src = rgb.Slice(y * width * 3, width * 3);
            var dst = span.Slice(pixelOffset + (height - 1 - y) * stride, width * 3);
            for (var x = 0; x < width * 3; x += 3)
            {
                dst[x] = src[x + 2];
                dst[x + 1] = src[x + 1];
                dst[x + 2] = src[x];
            }
        }

        return output;
    }

    // ---------------------------------------------------------------- PNG

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>8-bit truecolour PNG; every row uses the Sub filter, which suits rendered frames.</summary>
    public static byte[] EncodePng(int width, int height, ReadOnlySpan<byte> rgb)
    {
        CheckImage(width, height, rgb);
        var rowBytes = width * 3;
        var filtered = new byte[(rowBytes + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var src = rgb.Slice(y * rowBytes, rowBytes);
            var dst = filtered.AsSpan(y * (rowBytes + 1), rowBytes + 1);
            dst[0] = 1; // Sub: each byte minus the same channel of the pixel to its left
            for (var i = 0; i < rowBytes; i++)
                dst[i + 1] = (byte)(src[i] - (i >= 3 ? src[i - 3] : 0));
        }

        byte[] idat;
        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(filtered);
            idat = compressed.ToArray();
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type: truecolour
        // compression 0, filter method 0, no interlace

        using var output = new MemoryStream();
        output.Write(PngSignature);
        WritePngChunk(output, "IHDR", ihdr);
        WritePngChunk(output, "IDAT", idat);
        WritePngChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        Span<byte> typeBytes = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
            typeBytes[i] = (byte)type[i];
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32.Update(Crc32.Update(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        output.Write(word);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }

    // ---------------------------------------------------------------- JPEG

    /// <summary>
    /// Baseline (sequential, Huffman) JFIF JPEG, YCbCr without chroma subsampling so the UI text
    /// a screenshot usually shows stays sharp. Uses the example quantisation and Huffman tables of
    /// ITU-T T.81 Annex K, the quantisation scaled by <paramref name="quality"/> as libjpeg does.
    /// </summary>
    public static byte[] EncodeJpeg(int width, int height, ReadOnlySpan<byte> rgb, int quality = DefaultJpegQuality)
    {
        CheckImage(width, height, rgb);
        if (width > 0xFFFF || height > 0xFFFF)
            throw new ArgumentException("JPEG dimensions are limited to 65535.");
        quality = Math.Clamp(quality, 1, 100);

        var lumQ = ScaleQuant(Jpeg.LumQuant, quality);
        var chromQ = ScaleQuant(Jpeg.ChromQuant, quality);

        using var output = new MemoryStream();
        // SOI, APP0 (JFIF 1.1, no density, no thumbnail)
        output.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 });

        // DQT: both tables, in zigzag order
        output.Write(new byte[] { 0xFF, 0xDB, 0, 132 });
        output.WriteByte(0);
        for (var k = 0; k < 64; k++) output.WriteByte((byte)lumQ[Jpeg.ZigZag[k]]);
        output.WriteByte(1);
        for (var k = 0; k < 64; k++) output.WriteByte((byte)chromQ[Jpeg.ZigZag[k]]);

        // SOF0: 8-bit, 3 components at 1x1 sampling; Y uses table 0, Cb/Cr table 1
        output.Write(new byte[]
        {
            0xFF, 0xC0, 0, 17, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3,
            1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1,
        });

        // DHT: DC/AC luminance (id 0), DC/AC chrominance (id 1)
        WriteHuffmanTable(output, 0x00, Jpeg.DcLumBits, Jpeg.DcLumVals);
        WriteHuffmanTable(output, 0x10, Jpeg.AcLumBits, Jpeg.AcLumVals);
        WriteHuffmanTable(output, 0x01, Jpeg.DcChromBits, Jpeg.DcChromVals);
        WriteHuffmanTable(output, 0x11, Jpeg.AcChromBits, Jpeg.AcChromVals);

        // SOS: all three components, full spectral range
        output.Write(new byte[] { 0xFF, 0xDA, 0, 12, 3, 1, 0x00, 2, 0x11, 3, 0x11, 0, 63, 0 });

        var bits = new BitWriter(output);
        var dcLum = Jpeg.Codes(Jpeg.DcLumBits, Jpeg.DcLumVals);
        var acLum = Jpeg.Codes(Jpeg.AcLumBits, Jpeg.AcLumVals);
        var dcChrom = Jpeg.Codes(Jpeg.DcChromBits, Jpeg.DcChromVals);
        var acChrom = Jpeg.Codes(Jpeg.AcChromBits, Jpeg.AcChromVals);

        var yBlock = new float[64];
        var cbBlock = new float[64];
        var crBlock = new float[64];
        var coefficients = new int[64];
        int prevY = 0, prevCb = 0, prevCr = 0;

        for (var by = 0; by < height; by += 8)
        {
            for (var bx = 0; bx < width; bx += 8)
            {
                // Edge blocks repeat the last row/column, which keeps the padding cheap to encode.
                for (var y = 0; y < 8; y++)
                {
                    var row = Math.Min(by + y, height - 1) * width;
                    for (var x = 0; x < 8; x++)
                    {
                        var p = (row + Math.Min(bx + x, width - 1)) * 3;
                        float r = rgb[p], g = rgb[p + 1], b = rgb[p + 2];
                        var i = y * 8 + x;
                        yBlock[i] = 0.299f * r + 0.587f * g + 0.114f * b - 128f;
                        cbBlock[i] = -0.168736f * r - 0.331264f * g + 0.5f * b;
                        crBlock[i] = 0.5f * r - 0.418688f * g - 0.081312f * b;
                    }
                }

                prevY = EncodeBlock(bits, yBlock, lumQ, prevY, dcLum, acLum, coefficients);
                prevCb = EncodeBlock(bits, cbBlock, chromQ, prevCb, dcChrom, acChrom, coefficients);
                prevCr = EncodeBlock(bits, crBlock, chromQ, prevCr, dcChrom, acChrom, coefficients);
            }
        }

        bits.Flush();
        output.Write(new byte[] { 0xFF, 0xD9 }); // EOI
        return output.ToArray();
    }

    private static int[] ScaleQuant(int[] baseTable, int quality)
    {
        var scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
        var table = new int[64];
        for (var i = 0; i < 64; i++)
            table[i] = Math.Clamp((baseTable[i] * scale + 50) / 100, 1, 255);
        return table;
    }

    private static void WriteHuffmanTable(Stream output, byte classAndId, byte[] counts, byte[] values)
    {
        var length = 2 + 1 + 16 + values.Length;
        output.Write(new byte[] { 0xFF, 0xC4, (byte)(length >> 8), (byte)length, classAndId });
        output.Write(counts);
        output.Write(values);
    }

    private static int EncodeBlock(
        BitWriter bits, float[] block, int[] quant, int prevDc,
        (ushort Code, byte Length)[] dc, (ushort Code, byte Length)[] ac, int[] coefficients)
    {
        Jpeg.ForwardDct(block, coefficients, quant);

        var diff = coefficients[0] - prevDc;
        var category = Jpeg.Category(diff);
        bits.Write(dc[category]);
        if (category > 0)
            bits.Write(Jpeg.Magnitude(diff, category), category);

        var run = 0;
        for (var k = 1; k < 64; k++)
        {
            var value = coefficients[Jpeg.ZigZag[k]];
            if (value == 0)
            {
                run++;
                continue;
            }

            while (run > 15)
            {
                bits.Write(ac[0xF0]); // ZRL: sixteen zeros
                run -= 16;
            }

            var size = Jpeg.Category(value);
            bits.Write(ac[(run << 4) | size]);
            bits.Write(Jpeg.Magnitude(value, size), size);
            run = 0;
        }

        if (run > 0)
            bits.Write(ac[0x00]); // EOB
        return coefficients[0];
    }

    /// <summary>Entropy-coded segment writer: MSB first, 0xFF stuffed with 0x00, padded with 1s.</summary>
    private sealed class BitWriter
    {
        private readonly Stream _output;
        private uint _buffer;
        private int _count;

        public BitWriter(Stream output) => _output = output;

        public void Write((ushort Code, byte Length) code) => Write(code.Code, code.Length);

        public void Write(int value, int length)
        {
            if (length == 0)
                throw new InvalidOperationException("JPEG Huffman code missing for a symbol.");
            for (var i = length - 1; i >= 0; i--)
            {
                _buffer = (_buffer << 1) | (uint)((value >> i) & 1);
                if (++_count == 8)
                    Emit();
            }
        }

        public void Flush()
        {
            while (_count != 0)
            {
                _buffer = (_buffer << 1) | 1;
                if (++_count == 8)
                    Emit();
            }
        }

        private void Emit()
        {
            var b = (byte)_buffer;
            _output.WriteByte(b);
            if (b == 0xFF)
                _output.WriteByte(0);
            _buffer = 0;
            _count = 0;
        }
    }

    private static class Jpeg
    {
        /// <summary>Zigzag position k -> natural (row-major) index.</summary>
        public static readonly int[] ZigZag =
        {
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        };

        // T.81 Annex K.1, natural order.
        public static readonly int[] LumQuant =
        {
            16, 11, 10, 16, 24, 40, 51, 61,
            12, 12, 14, 19, 26, 58, 60, 55,
            14, 13, 16, 24, 40, 57, 69, 56,
            14, 17, 22, 29, 51, 87, 80, 62,
            18, 22, 37, 56, 68, 109, 103, 77,
            24, 35, 55, 64, 81, 104, 113, 92,
            49, 64, 78, 87, 103, 121, 120, 101,
            72, 92, 95, 98, 112, 100, 103, 99,
        };

        public static readonly int[] ChromQuant =
        {
            17, 18, 24, 47, 99, 99, 99, 99,
            18, 21, 26, 66, 99, 99, 99, 99,
            24, 26, 56, 99, 99, 99, 99, 99,
            47, 66, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
        };

        // T.81 Annex K.3: code-length counts (1..16) and symbols.
        public static readonly byte[] DcLumBits = { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
        public static readonly byte[] DcLumVals = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        public static readonly byte[] DcChromBits = { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };
        public static readonly byte[] DcChromVals = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        public static readonly byte[] AcLumBits = { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D };
        public static readonly byte[] AcLumVals =
        {
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
            0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
            0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
            0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
            0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
            0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
            0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
            0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
            0xF9, 0xFA,
        };

        public static readonly byte[] AcChromBits = { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77 };
        public static readonly byte[] AcChromVals =
        {
            0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
            0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
            0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
            0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
            0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
            0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
            0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
            0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
            0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
            0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
            0xF9, 0xFA,
        };

        /// <summary>Canonical Huffman codes (T.81 Annex C), indexed by symbol.</summary>
        public static (ushort Code, byte Length)[] Codes(byte[] counts, byte[] values)
        {
            var table = new (ushort, byte)[256];
            var code = 0;
            var v = 0;
            for (var length = 1; length <= 16; length++)
            {
                for (var i = 0; i < counts[length - 1]; i++)
                    table[values[v++]] = ((ushort)code++, (byte)length);
                code <<= 1;
            }
            if (v != values.Length)
                throw new InvalidOperationException("Huffman counts do not match the symbol list.");
            return table;
        }

        private static readonly float[] Cos = BuildCos();

        private static float[] BuildCos()
        {
            // Cos[u*8+x] = C(u)/2 * cos((2x+1)u*pi/16), so F = Cos * f * Cos^T is the 2-D DCT-II.
            var table = new float[64];
            for (var u = 0; u < 8; u++)
            {
                var c = u == 0 ? 1.0 / Math.Sqrt(2) : 1.0;
                for (var x = 0; x < 8; x++)
                    table[u * 8 + x] = (float)(c / 2 * Math.Cos((2 * x + 1) * u * Math.PI / 16));
            }
            return table;
        }

        /// <summary>2-D DCT of an 8x8 block, quantised and rounded, in natural order.</summary>
        public static void ForwardDct(float[] block, int[] output, int[] quant)
        {
            Span<float> rows = stackalloc float[64];
            for (var y = 0; y < 8; y++)
            {
                for (var u = 0; u < 8; u++)
                {
                    var sum = 0f;
                    for (var x = 0; x < 8; x++)
                        sum += Cos[u * 8 + x] * block[y * 8 + x];
                    rows[y * 8 + u] = sum;
                }
            }

            for (var v = 0; v < 8; v++)
            {
                for (var u = 0; u < 8; u++)
                {
                    var sum = 0f;
                    for (var y = 0; y < 8; y++)
                        sum += Cos[v * 8 + y] * rows[y * 8 + u];
                    var index = v * 8 + u;
                    output[index] = (int)MathF.Round(sum / quant[index], MidpointRounding.AwayFromZero);
                }
            }
        }

        /// <summary>Bits needed for |value| (the JPEG "SSSS" size category).</summary>
        public static int Category(int value)
        {
            value = Math.Abs(value);
            var n = 0;
            while (value != 0)
            {
                n++;
                value >>= 1;
            }
            return n;
        }

        /// <summary>Appended magnitude bits: the value itself, or value-1 in one's complement form when negative.</summary>
        public static int Magnitude(int value, int category) =>
            value >= 0 ? value : value + (1 << category) - 1;
    }
}
