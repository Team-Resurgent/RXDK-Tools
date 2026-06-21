namespace Rxdk.XbeImage.Internal;

internal sealed class AShaContext
{
    public uint FinishFlag;
    public readonly byte[] HashVal = new byte[20];
    public readonly uint[] State = new uint[5];
    public readonly uint[] Count = new uint[2];
    public readonly byte[] Buffer = new byte[64];
}

internal static class XbeSha1
{
    private const int Sha1Block = 64;

    public static void Init(AShaContext ctx)
    {
        ctx.State[0] = 0x67452301;
        ctx.State[1] = 0xEFCDAB89;
        ctx.State[2] = 0x98BADCFE;
        ctx.State[3] = 0x10325476;
        ctx.State[4] = 0xC3D2E1F0;
        ctx.Count[0] = ctx.Count[1] = 0;
        ctx.FinishFlag = 0;
    }

    public static void Update(AShaContext ctx, ReadOnlySpan<byte> data)
    {
        if (ctx.FinishFlag != 0)
        {
            return;
        }

        var idx = (int)((ctx.Count[0] >> 3) & 63);
        if ((ctx.Count[0] += (uint)(data.Length << 3)) < (uint)(data.Length << 3))
        {
            ctx.Count[1]++;
        }

        ctx.Count[1] += (uint)(data.Length >> 29);

        var offset = 0;
        if (idx + data.Length > 63)
        {
            var i = 64 - idx;
            data[..i].CopyTo(ctx.Buffer.AsSpan(idx));
            Transform(ctx.State, ctx.Buffer);
            for (; i + 63 < data.Length; i += 64)
            {
                Transform(ctx.State, data.Slice(i, 64));
            }

            idx = 0;
            offset = i;
        }

        data[offset..].CopyTo(ctx.Buffer.AsSpan(idx));
    }

    public static void Final(AShaContext ctx, Span<byte> digest)
    {
        Pad(ctx);
        for (var i = 0; i < 20; i++)
        {
            digest[i] = (byte)((ctx.State[i >> 2] >> ((3 - (i & 3)) * 8)) & 255);
        }

        ctx.FinishFlag = 1;
    }

    public static void CalcDigest(ReadOnlySpan<byte> message, Span<byte> digest)
    {
        var ctx = new AShaContext();
        Init(ctx);
        var lengthBytes = BitConverter.GetBytes(message.Length);
        Update(ctx, lengthBytes);
        Update(ctx, message);
        Final(ctx, digest);
    }

    public static void CalcDigestRaw(ReadOnlySpan<byte> message, Span<byte> digest)
    {
        var ctx = new AShaContext();
        Init(ctx);
        Update(ctx, message);
        Final(ctx, digest);
    }

    private static void Pad(AShaContext ctx)
    {
        var idx = (int)((ctx.Count[0] >> 3) & 63);
        ctx.Buffer[idx++] = 0x80;
        if (idx > 56)
        {
            ctx.Buffer.AsSpan(idx).Clear();
            Transform(ctx.State, ctx.Buffer);
            idx = 0;
        }

        ctx.Buffer.AsSpan(idx, 56 - idx).Clear();
        ctx.Buffer[56] = (byte)(ctx.Count[1] >> 24);
        ctx.Buffer[57] = (byte)(ctx.Count[1] >> 16);
        ctx.Buffer[58] = (byte)(ctx.Count[1] >> 8);
        ctx.Buffer[59] = (byte)ctx.Count[1];
        ctx.Buffer[60] = (byte)(ctx.Count[0] >> 24);
        ctx.Buffer[61] = (byte)(ctx.Count[0] >> 16);
        ctx.Buffer[62] = (byte)(ctx.Count[0] >> 8);
        ctx.Buffer[63] = (byte)ctx.Count[0];
        Transform(ctx.State, ctx.Buffer);
    }

    private static void Transform(uint[] state, ReadOnlySpan<byte> block)
    {
        Span<uint> w = stackalloc uint[80];
        for (var i = 0; i < 16; i++)
        {
            w[i] = ((uint)block[i * 4] << 24) |
                   ((uint)block[i * 4 + 1] << 16) |
                   ((uint)block[i * 4 + 2] << 8) |
                   block[i * 4 + 3];
        }

        for (var i = 16; i < 80; i++)
        {
            var v = w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16];
            w[i] = (v << 1) | (v >> 31);
        }

        var a = state[0];
        var b = state[1];
        var c = state[2];
        var d = state[3];
        var e = state[4];

        for (var i = 0; i < 20; i++)
        {
            var t = Rol(a, 5) + ((b & c) | (~b & d)) + e + w[i] + 0x5A827999;
            e = d;
            d = c;
            c = Rol(b, 30);
            b = a;
            a = t;
        }

        for (var i = 20; i < 40; i++)
        {
            var t = Rol(a, 5) + (b ^ c ^ d) + e + w[i] + 0x6ED9EBA1;
            e = d;
            d = c;
            c = Rol(b, 30);
            b = a;
            a = t;
        }

        for (var i = 40; i < 60; i++)
        {
            var t = Rol(a, 5) + ((b & c) | (b & d) | (c & d)) + e + w[i] + 0x8F1BBCDC;
            e = d;
            d = c;
            c = Rol(b, 30);
            b = a;
            a = t;
        }

        for (var i = 60; i < 80; i++)
        {
            var t = Rol(a, 5) + (b ^ c ^ d) + e + w[i] + 0xCA62C1D6;
            e = d;
            d = c;
            c = Rol(b, 30);
            b = a;
            a = t;
        }

        state[0] += a;
        state[1] += b;
        state[2] += c;
        state[3] += d;
        state[4] += e;
    }

    private static uint Rol(uint value, int bits) => (value << bits) | (value >> (32 - bits));
}
