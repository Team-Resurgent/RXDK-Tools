using System.Runtime.InteropServices;

namespace Rxdk.XbeImage.Internal;

internal static class XbeRsaBsafe
{
    private const uint Rsa2 = 0x3241_5352; // 'RSA2'

    private static readonly byte[] ShaEncodings =
    {
        0x0F, 0x14, 0x04, 0x00, 0x05, 0x1A, 0x02, 0x03, 0x0E, 0x2B, 0x05, 0x06, 0x09, 0x30, 0x21, 0x30
    };

    public static void SignDigest(ReadOnlySpan<byte> digest, ReadOnlySpan<byte> privateKey, Span<byte> signature)
    {
        var header = MemoryMarshal.Read<BSafePrvKeyHeader>(privateKey);
        if (header.Magic != Rsa2)
        {
            throw new XbeImageException("Invalid RSA private key.");
        }

        var keyLen = (int)header.KeyLen;
        Span<byte> input = stackalloc byte[keyLen];
        Span<byte> output = stackalloc byte[keyLen];
        input.Clear();
        output.Clear();

        ApplyPkcs1SigningFormat(privateKey, digest, input);
        DecPrivate(privateKey, input, output);

        var sigLen = (int)((header.BitLen + 7) / 8);
        output[..sigLen].CopyTo(signature);
    }

    private static void ApplyPkcs1SigningFormat(ReadOnlySpan<byte> key, ReadOnlySpan<byte> digest, Span<byte> pkcs1)
    {
        var header = MemoryMarshal.Read<BSafePrvKeyHeader>(key);
        pkcs1[(int)header.DataLen - 1] = 0x01;
        pkcs1[..(int)(header.DataLen - 1)].Fill(0xFF);

        for (var i = 0; i < XbeImageConstants.DigestLength; i++)
        {
            pkcs1[i] = digest[XbeImageConstants.DigestLength - (i + 1)];
        }

        var start = XbeImageConstants.DigestLength;
        var endIndex = 0;
        var length = ShaEncodings[endIndex++];
        while (length-- > 0)
        {
            pkcs1[start++] = ShaEncodings[endIndex++];
        }

        pkcs1[start] = 0;
    }

    private static void DecPrivate(ReadOnlySpan<byte> key, ReadOnlySpan<byte> partIn, Span<byte> partOut)
    {
        var header = MemoryMarshal.Read<BSafePrvKeyHeader>(key);
        if (header.Magic != Rsa2)
        {
            throw new XbeImageException("Invalid RSA private key.");
        }

        if (header.PubExp == 1)
        {
            partIn[..(int)header.KeyLen].CopyTo(partOut);
            return;
        }

        var keyLen = (int)header.KeyLen;
        var half = keyLen >> 1;
        var offset = Marshal.SizeOf<BSafePrvKeyHeader>();
        var modulus = key.Slice(offset, keyLen);
        var prime1 = key.Slice(offset + keyLen, half);
        var prime2 = key.Slice(offset + keyLen + half, half);
        var exp1 = key.Slice(offset + keyLen + half * 2, half);
        var exp2 = key.Slice(offset + keyLen + half * 3, half);
        var coef = key.Slice(offset + keyLen + half * 4, half);

        var pdWords = KeyPDWords(header.BitLen);
        var fullWords = keyLen / 4;
        Span<uint> partInWords = stackalloc uint[fullWords];
        partInWords.Clear();
        MemoryMarshal.Cast<byte, uint>(partIn[..keyLen]).CopyTo(partInWords);

        Span<uint> output = stackalloc uint[fullWords];
        output.Clear();

        if (!XbeRsaMath.BenalohModRoot(
                output,
                partInWords,
                MemoryMarshal.Cast<byte, uint>(prime1),
                MemoryMarshal.Cast<byte, uint>(prime2),
                MemoryMarshal.Cast<byte, uint>(exp1),
                MemoryMarshal.Cast<byte, uint>(exp2),
                MemoryMarshal.Cast<byte, uint>(coef),
                pdWords))
        {
            throw new XbeImageException("RSA private decryption failed.");
        }

        MemoryMarshal.AsBytes(output).CopyTo(partOut[..keyLen]);
    }

    private static int KeyPDWords(uint bitLen)
    {
        var half = bitLen >> 1;
        var pd = (int)((half >> 5) + 1);
        if ((half & 31) != 0)
        {
            pd++;
        }

        return pd;
    }
}
