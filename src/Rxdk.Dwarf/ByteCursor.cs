// SPDX-License-Identifier: GPL-3.0-or-later
// Part of RXDK-Tools - see LICENSE for the full GNU GPL v3.

using System.Text;

namespace Rxdk.Dwarf;

/// <summary>
/// A forward cursor over a DWARF section. Fixed-size integers follow the image's
/// byte order (little-endian for the original Xbox's x86 PE; big-endian for a
/// PowerPC ELF) - set via <paramref name="bigEndian"/>. LEB128 is byte-order
/// independent. Ported from RXDK-360's ByteCursor, made endian-aware.
/// </summary>
internal sealed class ByteCursor
{
    private readonly byte[] _b;
    private readonly bool _be;
    public int Pos;

    public ByteCursor(byte[] bytes, bool bigEndian = false, int pos = 0)
    {
        _b = bytes; _be = bigEndian; Pos = pos;
    }

    public bool AtEnd => Pos >= _b.Length;
    public int Length => _b.Length;

    public byte U8() => _b[Pos++];

    public ushort U16()
    {
        ushort v = _be
            ? (ushort)((_b[Pos] << 8) | _b[Pos + 1])
            : (ushort)(_b[Pos] | (_b[Pos + 1] << 8));
        Pos += 2; return v;
    }

    public uint U32()
    {
        uint v = _be
            ? (uint)((_b[Pos] << 24) | (_b[Pos + 1] << 16) | (_b[Pos + 2] << 8) | _b[Pos + 3])
            : (uint)(_b[Pos] | (_b[Pos + 1] << 8) | (_b[Pos + 2] << 16) | (_b[Pos + 3] << 24));
        Pos += 4; return v;
    }

    public ulong U64()
    {
        if (_be) { ulong hi = U32(); ulong lo = U32(); return (hi << 32) | lo; }
        ulong low = U32(); ulong high = U32(); return (high << 32) | low;
    }

    /// <summary>Read an unsigned integer of the given size (1/2/4/8).</summary>
    public ulong UPtr(int size) => size switch
    {
        1 => U8(),
        2 => U16(),
        4 => U32(),
        8 => U64(),
        _ => throw new NotSupportedException($"pointer size {size}")
    };

    public ulong ULeb()
    {
        ulong result = 0; int shift = 0; byte b;
        do { b = _b[Pos++]; result |= (ulong)(b & 0x7f) << shift; shift += 7; }
        while ((b & 0x80) != 0);
        return result;
    }

    public long SLeb()
    {
        long result = 0; int shift = 0; byte b;
        do { b = _b[Pos++]; result |= (long)(b & 0x7f) << shift; shift += 7; }
        while ((b & 0x80) != 0);
        if (shift < 64 && (b & 0x40) != 0) result |= -1L << shift;   // sign-extend
        return result;
    }

    /// <summary>NUL-terminated string at the cursor.</summary>
    public string CStr()
    {
        int start = Pos;
        while (Pos < _b.Length && _b[Pos] != 0) Pos++;
        string s = Encoding.UTF8.GetString(_b, start, Pos - start);
        if (Pos < _b.Length) Pos++;   // skip the NUL
        return s;
    }

    public byte[] Bytes(int n)
    {
        var outb = new byte[n];
        Array.Copy(_b, Pos, outb, 0, n);
        Pos += n; return outb;
    }

    public void Skip(int n) => Pos += n;
}

/// <summary>A NUL-terminated string at an absolute offset in a string section.</summary>
internal static class StrSection
{
    public static string At(byte[] section, uint offset)
    {
        if (section.Length == 0 || offset >= section.Length) return "";
        int end = (int)offset;
        while (end < section.Length && section[end] != 0) end++;
        return Encoding.UTF8.GetString(section, (int)offset, end - (int)offset);
    }
}
