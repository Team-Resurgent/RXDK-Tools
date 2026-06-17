namespace Rxdk.Xbdm.Managed;

internal static class XbcCrypto
{
    private static readonly uint[] RoundConstants =
    [
        0x283C481D, 0x9AD82AA1, 0x85A5E1F9, 0x1B23963C,
        0xF70B4975, 0xDFDC02C7, 0xF29176FC, 0x6B04BD38,
    ];

    internal static ulong Cross(ulong key, ulong data)
    {
        var result = 0x2718281831415926UL;
        HashBlock(ref result, key);
        var temp = result;
        HashBlock(ref temp, data);
        HashBlock(ref result, temp);
        return result;
    }

    internal static void HashData(ref ulong hash, ReadOnlySpan<byte> data)
    {
        var offset = 0;
        Span<byte> block = stackalloc byte[8];
        while (offset + 8 <= data.Length)
        {
            data.Slice(offset, 8).CopyTo(block);
            HashBlock(ref hash, BitConverter.ToUInt64(block));
            offset += 8;
        }

        var remaining = data.Length - offset;
        if (remaining == 0)
            return;

        block.Clear();
        data[offset..].CopyTo(block);
        HashBlock(ref hash, BitConverter.ToUInt64(block));
    }

    internal static void HashDataAsciiPassword(ref ulong hash, string password) =>
        HashData(ref hash, System.Text.Encoding.ASCII.GetBytes(password));

    private static void HashBlock(ref ulong hash, ulong data)
    {
        var temp = data;
        EncryptCore(ref hash, ref temp);
        hash = data ^ temp;
    }

    private static void EncryptCore(ref ulong key, ref ulong block)
    {
        for (var i = 0; i < 8; i++)
        {
            var low = block;
            var high = block >> 32;
            var keyLow = (uint)key;
            var keyHigh = (uint)(key >> 32);

            var newLow = (uint)low;
            newLow ^= (uint)((high >> 5) + RoundConstants[i] + (~high << 6) + (high ^ keyLow));

            var newHigh = (uint)high;
            newHigh ^= (uint)((~low >> 5) + RoundConstants[7 - i] + (low << 6) + (low ^ keyHigh));

            block = ((ulong)newHigh << 32) | newLow;
        }
    }
}
