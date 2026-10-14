using System.Buffers.Binary;
using System.Text;

namespace Rxdk.Engine.Build;

/// <summary>
/// Checks a linked title's .eh_frame bracket. libunwind finds the title's DWARF unwind tables
/// from &amp;__eh_frame_start and &amp;__eh_frame_end alone, so both markers must sit at the edges of
/// the merged .eh_frame section: an FDE outside them is invisible, and a throw through that
/// function terminates on the console. The link places the markers by section ordering
/// (see XdkLink), and this verifies the result from the PE's section table and COFF symbols.
/// </summary>
public static class EhFrameCheck
{
    /// <summary>
    /// Returns null when the markers bracket .eh_frame exactly, or when the image has no
    /// markers (a title that does not link libcpp) or no COFF symbol table to read them from.
    /// Otherwise returns a message describing the mismatch.
    /// </summary>
    public static string? Verify(ReadOnlySpan<byte> pe)
    {
        if (pe.Length < 0x40 || pe[0] != (byte)'M' || pe[1] != (byte)'Z') return "not a PE image";
        int peOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(pe[0x3C..]);
        if (peOff + 24 > pe.Length || !pe.Slice(peOff, 4).SequenceEqual("PE\0\0"u8)) return "not a PE image";

        var coff = pe[(peOff + 4)..];
        int nsec = BinaryPrimitives.ReadUInt16LittleEndian(coff[2..]);
        int symPtr = (int)BinaryPrimitives.ReadUInt32LittleEndian(coff[8..]);
        int nsym = (int)BinaryPrimitives.ReadUInt32LittleEndian(coff[12..]);
        int optSize = BinaryPrimitives.ReadUInt16LittleEndian(coff[16..]);
        if (symPtr == 0 || nsym == 0) return null;
        int strTab = symPtr + nsym * 18;
        if (strTab > pe.Length) return "COFF symbol table runs past the end of the image";

        int secTable = peOff + 24 + optSize;
        var sections = new (string Name, uint VirtualSize)[nsec];
        for (int i = 0; i < nsec; i++)
        {
            var s = pe.Slice(secTable + i * 40, 40);
            sections[i] = (ShortName(s[..8], pe, strTab), BinaryPrimitives.ReadUInt32LittleEndian(s[8..]));
        }

        (uint Value, int Section)? start = null, end = null;
        for (int i = 0; i < nsym; i++)
        {
            var e = pe.Slice(symPtr + i * 18, 18);
            string name = ShortName(e[..8], pe, strTab, symbol: true);
            var value = (BinaryPrimitives.ReadUInt32LittleEndian(e[8..]), (int)BinaryPrimitives.ReadInt16LittleEndian(e[12..]));
            if (name == "___eh_frame_start") start = value;
            else if (name == "___eh_frame_end") end = value;
            i += e[17]; // skip auxiliary records
        }
        if (start is null && end is null) return null;
        if (start is null || end is null) return "only one of ___eh_frame_start/___eh_frame_end is defined";

        var (sv, ss) = start.Value;
        var (ev, es) = end.Value;
        if (ss < 1 || ss > nsec || es != ss)
            return "___eh_frame_start and ___eh_frame_end are not in the same section";
        var (secName, size) = sections[ss - 1];
        if (!secName.StartsWith(".eh_fram", StringComparison.Ordinal))
            return $"___eh_frame_start is in {secName}, not .eh_frame";
        if (sv != 0 || ev != size)
            return $".eh_frame is 0x{size:X} bytes but the markers bracket +0x{sv:X}..+0x{ev:X}; " +
                   "FDEs outside them are invisible to libunwind";
        return null;
    }

    // An 8-byte COFF name field: inline, or a string-table reference ("/123" for a section,
    // four zero bytes then an offset for a symbol).
    private static string ShortName(ReadOnlySpan<byte> field, ReadOnlySpan<byte> pe, int strTab, bool symbol = false)
    {
        int offset = -1;
        if (symbol && BinaryPrimitives.ReadUInt32LittleEndian(field) == 0)
            offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(field[4..]);
        else if (!symbol && field[0] == (byte)'/' && int.TryParse(Encoding.ASCII.GetString(field[1..]).TrimEnd('\0'), out var o))
            offset = o;
        if (offset < 0) return Encoding.ASCII.GetString(field).TrimEnd('\0');
        var rest = pe[(strTab + offset)..];
        int len = rest.IndexOf((byte)0);
        return Encoding.ASCII.GetString(len < 0 ? rest : rest[..len]);
    }
}
