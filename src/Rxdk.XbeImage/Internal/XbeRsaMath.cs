namespace Rxdk.XbeImage.Internal;

/// <summary>
/// Port of <c>xrsa_math.c</c> + <c>xrsa_benaloh.c</c> bignum primitives (explicit limb counts).
/// </summary>
internal static class XbeRsaMath
{
    private const uint DigitHibit = 0x8000_0000;
    private const int MaxWords = 128;

    public static void SetValDword(Span<uint> num, uint val, int len)
    {
        if (len <= 0)
            return;

        num[0] = val;
        var fill = (val & DigitHibit) != 0 ? 0xFFu : 0u;
        for (var i = 1; i < len; i++)
            num[i] = fill;
    }

    public static uint BitLen(ReadOnlySpan<uint> value, int len)
    {
        var idx = TrimLen(value, len);
        if (idx == 0)
            return 0;

        var top = value[idx - 1];
        var bits = (uint)idx * 32;
        while ((top & DigitHibit) == 0)
        {
            top <<= 1;
            bits--;
        }

        return bits;
    }

    public static int Compare(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, int n)
    {
        if (n == 0)
            return 0;

        for (var i = n - 1; i > 0; i--)
        {
            if (a[i] > b[i])
                return 1;
            if (a[i] < b[i])
                return -1;
        }

        if (a[0] > b[0])
            return 1;
        return a[0] < b[0] ? -1 : 0;
    }

    public static uint Add(Span<uint> a, ReadOnlySpan<uint> b, ReadOnlySpan<uint> c, int n)
    {
        ulong carry = 0;
        for (var i = 0; i < n; i++)
        {
            var sum = (ulong)b[i] + c[i] + carry;
            a[i] = (uint)sum;
            carry = sum >> 32;
        }

        return (uint)carry;
    }

    public static uint Sub(Span<uint> a, ReadOnlySpan<uint> b, ReadOnlySpan<uint> c, int n)
    {
        ulong borrow = 0;
        for (var i = 0; i < n; i++)
        {
            var bv = (ulong)c[i] + borrow;
            if ((ulong)b[i] < bv)
            {
                a[i] = (uint)((ulong)b[i] + (1UL << 32) - bv);
                borrow = 1;
            }
            else
            {
                a[i] = (uint)((ulong)b[i] - bv);
                borrow = 0;
            }
        }

        return (uint)borrow;
    }

    public static uint BaseMult(Span<uint> a, uint b, ReadOnlySpan<uint> c, int n)
    {
        if (b == 1)
        {
            c[..n].CopyTo(a);
            return 0;
        }

        ulong carry = 0;
        for (var i = 0; i < n; i++)
        {
            var prod = (ulong)b * c[i] + carry;
            a[i] = (uint)prod;
            carry = prod >> 32;
        }

        return (uint)carry;
    }

    public static uint Accumulate(Span<uint> a, uint b, ReadOnlySpan<uint> c, int n)
    {
        if (b == 0)
            return 0;

        if (b == 1)
            return Add(a, a, c, n);

        ulong carry = 0;
        for (var i = 0; i < n; i++)
        {
            var prod = (ulong)b * c[i];
            var tLo = (uint)prod + (uint)carry;
            var tHi = (uint)(prod >> 32) + (tLo < (uint)prod ? 1u : 0u);
            var ai = a[i];
            var sum = (ulong)ai + tLo;
            a[i] = (uint)sum;
            tHi += (uint)(sum >> 32);
            carry = tHi;
        }

        return (uint)carry;
    }

    public static void AccumulateSquares(Span<uint> a, ReadOnlySpan<uint> b, int blen)
    {
        ulong pending = 0;
        for (var i = 0; i < blen; i++)
        {
            var sq = (ulong)b[i] * b[i];
            var t = pending + sq;
            var aLo = a[i * 2];
            var aHi = a[i * 2 + 1];
            var sumLo = (ulong)aLo + (uint)t;
            var sumHi = (t >> 32) + (sumLo >> 32) + aHi;
            pending = sumHi >> 32;
            a[i * 2] = (uint)sumLo;
            a[i * 2 + 1] = (uint)sumHi;
        }
    }

    public static void Multiply(Span<uint> a, ReadOnlySpan<uint> b, ReadOnlySpan<uint> c, int n)
    {
        a[..n].Clear();
        var cLen = TrimLen(c, n);
        for (var i = 0; i < n; i++)
            a[i + cLen] = Accumulate(a.Slice(i), b[i], c[..cLen], cLen);
    }

    public static void Square(Span<uint> a, ReadOnlySpan<uint> b, int n)
    {
        a[..(n * 2)].Clear();
        var bLen = TrimLen(b, n);
        if (bLen > 1)
        {
            var i = bLen - 1;
            var pB = 0;
            var pA = 1;
            do
            {
                a[pA + i] = Accumulate(a.Slice(pA), b[pB], b.Slice(pB + 1, i), i);
                i--;
                pB++;
                pA += 2;
            } while (i > 0);
        }

        Add(a, a, a, n * 2);
        AccumulateSquares(a, b, bLen);
    }

    public static bool Mod(Span<uint> a, ReadOnlySpan<uint> modulus, Span<uint> result, int t, int n)
    {
        if (n == 0 || t == 0)
            return false;

        var aLen = TrimLen(a, t);
        var mLen = TrimLen(modulus, n);
        if (modulus[0] == 0)
            return false;

        if (aLen < mLen)
        {
            a[..n].CopyTo(result);
            return true;
        }

        Span<uint> work = stackalloc uint[aLen + 2 * mLen + 3];
        work.Clear();

        var prod = work[..mLen];
        var prodHi = work.Slice(mLen, 1);
        var rem = work.Slice(mLen + 1, mLen + 1);
        var modW = work.Slice(2 * mLen + 2, aLen + 1);

        modulus[..mLen].CopyTo(rem);
        rem[mLen] = 0;
        a[..aLen].CopyTo(modW);
        modW[aLen] = 0;

        var count = (int)aLen - (int)mLen;
        if (count >= 0)
        {
            var tailOffset = count;
            var subPosOffset = aLen;
            while (count >= 0)
            {
                uint q;
                if (mLen > 1)
                    q = EstimateQuotient(modW[subPosOffset], modW[subPosOffset - 1], modulus[mLen - 1], modulus[mLen - 2]);
                else
                    q = EstimateQuotient(modW[subPosOffset], 0, modulus[0], 0);

                if (q == 0)
                    q = 1;

                var carry = BaseMult(prod, q, modulus[..mLen], mLen);
                prodHi[0] = carry;

                var prodSlice = work.Slice(0, mLen + 1);
                var tail = modW.Slice(tailOffset, mLen + 1);

                while (WordsGreater(prodSlice, tail))
                    Sub(prodSlice, prodSlice, rem, mLen + 1);

                Sub(tail, tail, prodSlice, mLen + 1);

                if (Compare(tail, rem, mLen + 1) >= 0)
                    continue;

                count--;
                tailOffset--;
                subPosOffset--;
            }
        }

        modW[..mLen].CopyTo(result);
        if (n > mLen)
            result.Slice(mLen, n - mLen).Clear();

        return true;
    }

    public static bool BenalohModExp(Span<uint> a, ReadOnlySpan<uint> b, ReadOnlySpan<uint> c, ReadOnlySpan<uint> d, int len)
    {
        if (len == 0 || len > MaxWords)
            return false;

        var bits = BitLen(c, len);
        if (bits == 0)
        {
            SetValDword(a, 1, len);
            return true;
        }

        Span<uint> prod = stackalloc uint[len * 2];
        Span<uint> baseValue = stackalloc uint[len];
        Span<uint> result = stackalloc uint[len];

        b[..len].CopyTo(prod);
        prod.Slice(len, len).Clear();
        if (!Mod(prod, d, baseValue, 2 * len, len))
            return false;

        SetValDword(result, 1, len);
        for (var i = (int)bits - 1; i >= 0; i--)
        {
            Square(prod, result, len);
            if (!Mod(prod, d, result, 2 * len, len))
                return false;

            if (BitAt(c, (uint)i, len))
            {
                Multiply(prod, result, baseValue, len);
                if (!Mod(prod, d, result, 2 * len, len))
                    return false;
            }
        }

        result.CopyTo(a);
        return true;
    }

    public static bool BenalohModRoot(
        Span<uint> m,
        ReadOnlySpan<uint> c,
        ReadOnlySpan<uint> pp,
        ReadOnlySpan<uint> qq,
        ReadOnlySpan<uint> dp,
        ReadOnlySpan<uint> dq,
        ReadOnlySpan<uint> cr,
        int pSize)
    {
        var full = 2 * pSize;
        if (pSize == 0 || full > MaxWords * 2 || pSize > MaxWords)
            return false;

        Span<uint> cTmp = stackalloc uint[full];
        Span<uint> mP = stackalloc uint[pSize];
        Span<uint> mQ = stackalloc uint[pSize];
        Span<uint> diff = stackalloc uint[pSize];
        Span<uint> h = stackalloc uint[pSize];
        Span<uint> prod = stackalloc uint[full];

        c[..full].CopyTo(cTmp);
        if (!Mod(cTmp, pp, mP, full, pSize))
            return false;

        if (!BenalohModExp(mP, mP, dp, pp, pSize))
            return false;

        c[..full].CopyTo(cTmp);
        if (!Mod(cTmp, qq, mQ, full, pSize))
            return false;

        if (!BenalohModExp(mQ, mQ, dq, qq, pSize))
            return false;

        if (Sub(diff, mP, mQ, pSize) != 0)
        {
            while (Add(diff, diff, pp, pSize) != 0)
            {
            }
        }

        Multiply(prod, diff, cr, pSize);
        if (!Mod(prod, pp, h, full, pSize))
            return false;

        Multiply(prod, h, qq, pSize);
        cTmp.Clear();
        mQ.CopyTo(cTmp);
        Add(m, prod, cTmp, full);
        return true;
    }

    private static int TrimLen(ReadOnlySpan<uint> value, int n)
    {
        while (n > 0 && value[n - 1] == 0)
            n--;

        return n == 0 ? 1 : n;
    }

    private static bool BitAt(ReadOnlySpan<uint> value, uint index, int len) =>
        index < (uint)len * 32 && ((value[(int)(index >> 5)] >> (int)(index & 31)) & 1u) != 0;

    private static bool WordsGreater(ReadOnlySpan<uint> prodWords, ReadOnlySpan<uint> tail)
    {
        for (var i = prodWords.Length - 1; i >= 0; i--)
        {
            if (prodWords[i] > tail[i])
                return true;
            if (prodWords[i] < tail[i])
                return false;
        }

        return false;
    }

    private static uint EstimateQuotient(uint n0, uint n1, uint d0, uint d1)
    {
        if (d1 == 0 || (int)d0 < 0)
        {
            if (n0 >= d0)
                return 0xFFFF_FFFFu;
            return (uint)(((ulong)n0 << 32 | n1) / d0);
        }

        if (n0 > d0 || (n0 == d0 && n1 >= d1))
            return 0xFFFF_FFFFu;

        var quotient = 0u;
        var bit = 0x8000_0000u;
        var numHi = n0;
        var numLo = n1;
        do
        {
            numHi = (numHi << 1) | (numLo >> 31);
            numLo <<= 1;
            if (numHi > d0 || (numHi == d0 && numLo >= d1))
            {
                if (numLo < d1)
                    numHi--;
                numLo -= d1;
                numHi -= d0;
                quotient |= bit;
            }

            bit >>= 1;
        } while (bit != 0);

        return quotient;
    }
}
