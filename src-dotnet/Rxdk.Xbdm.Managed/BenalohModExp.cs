using System.Numerics;

namespace Rxdk.Xbdm.Managed;

internal static class BenalohModExp
{
    internal static void ModExp(Span<uint> result, ReadOnlySpan<uint> baseValue, ReadOnlySpan<uint> exponent, ReadOnlySpan<uint> modulus, int len)
    {
        if (len <= 0 || result.Length < len || baseValue.Length < len || exponent.Length < len || modulus.Length < len)
            throw new InvalidOperationException("Invalid Benaloh length.");

        if (IsZero(modulus, len))
            throw new InvalidOperationException("Modulus cannot be zero.");

        var baseInt = ToBigInteger(baseValue, len);
        var expInt = ToBigInteger(exponent, len);
        var modInt = ToBigInteger(modulus, len);
        var value = BigInteger.ModPow(baseInt, expInt, modInt);
        FromBigInteger(value, result, len);
    }

    private static bool IsZero(ReadOnlySpan<uint> value, int len)
    {
        for (var i = 0; i < len; i++)
        {
            if (value[i] != 0)
                return false;
        }

        return true;
    }

    private static BigInteger ToBigInteger(ReadOnlySpan<uint> words, int len)
    {
        Span<byte> bytes = stackalloc byte[len * 4];
        for (var i = 0; i < len; i++)
            BitConverter.TryWriteBytes(bytes.Slice(i * 4, 4), words[i]);

        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static void FromBigInteger(BigInteger value, Span<uint> destination, int len)
    {
        if (value.Sign < 0)
            throw new InvalidOperationException("Negative modexp result.");

        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        destination[..len].Clear();
        var copy = Math.Min(bytes.Length, len * 4);
        for (var i = 0; i < copy; i++)
        {
            var wordIndex = i / 4;
            var shift = (i % 4) * 8;
            destination[wordIndex] |= (uint)bytes[i] << shift;
        }
    }
}
