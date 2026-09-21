// SPDX-License-Identifier: GPL-3.0-or-later
// Part of RXDK-Tools - see LICENSE for the full GNU GPL v3.

namespace Rxdk.Dwarf;

/// <summary>
/// Minimal PE reader - just enough to pull the DWARF <c>.debug_*</c> sections out
/// of a linked original-Xbox title (.exe, i686 little-endian). clang/lld emit the
/// DWARF sections into the PE; because their names exceed 8 chars they land as
/// COFF long-name <c>/N</c> entries pointing into the string table, which this
/// resolves. The PE replaces the ELF the 360 side reads (<see cref="ISectionSource"/>).
/// </summary>
public sealed class PeImage : ISectionSource
{
    private readonly byte[] _b;
    private readonly Dictionary<string, (int off, int size)> _sections = new(StringComparer.Ordinal);

    public bool IsBigEndian => false;              // x86 PE is little-endian
    public ushort Machine { get; private set; }
    public ulong Entry { get; private set; }

    public PeImage(byte[] bytes)
    {
        _b = bytes;
        if (_b.Length < 0x40 || _b[0] != (byte)'M' || _b[1] != (byte)'Z')
            throw new InvalidDataException("not a PE/MZ file");
        int peOff = (int)U32(0x3C);
        if (peOff <= 0 || peOff + 24 > _b.Length ||
            _b[peOff] != (byte)'P' || _b[peOff + 1] != (byte)'E' || _b[peOff + 2] != 0 || _b[peOff + 3] != 0)
            throw new InvalidDataException("missing PE signature");

        int coff = peOff + 4;                      // COFF file header
        Machine = U16(coff + 0);
        int numSections = U16(coff + 2);
        uint symTabPtr = U32(coff + 8);
        uint numSyms = U32(coff + 12);
        int optSize = U16(coff + 16);
        int optHdr = coff + 20;
        if (optSize >= 16 && optHdr + 20 <= _b.Length)
        {
            // AddressOfEntryPoint (RVA) at optional-header offset 16, in PE32/PE32+.
            uint entryRva = U32(optHdr + 16);
            uint imageBase = optSize >= 32 && _b[optHdr] == 0x0b && _b[optHdr + 1] == 0x01
                ? U32(optHdr + 28) : 0;             // PE32 ImageBase (VA = base + rva)
            Entry = imageBase + entryRva;
        }

        // COFF string table follows the symbol table (each symbol is 18 bytes).
        long strTab = symTabPtr != 0 ? symTabPtr + (long)numSyms * 18 : 0;

        int sect = optHdr + optSize;
        for (int i = 0; i < numSections; i++)
        {
            int h = sect + i * 40;
            if (h + 40 > _b.Length) break;
            string name = SectionName(h, strTab);
            if (name.Length == 0) continue;
            int vsize = (int)U32(h + 8);    // VirtualSize
            int rawSize = (int)U32(h + 16); // SizeOfRawData
            int rawPtr = (int)U32(h + 20);  // PointerToRawData
            // Debug sections: VirtualSize is the true length; SizeOfRawData is file-aligned
            // padding. Use VirtualSize when set and not larger than the on-disk bytes.
            int size = (vsize > 0 && vsize <= rawSize) ? vsize : rawSize;
            if (rawPtr > 0 && size > 0 && rawPtr + size <= _b.Length && !_sections.ContainsKey(name))
                _sections[name] = (rawPtr, size);
        }
    }

    public static PeImage Load(string path) => new(File.ReadAllBytes(path));

    public byte[] Section(string name)
    {
        if (!_sections.TryGetValue(name, out var s)) return Array.Empty<byte>();
        var outb = new byte[s.size];
        Array.Copy(_b, s.off, outb, 0, s.size);
        return outb;
    }

    public bool HasSection(string name) => _sections.ContainsKey(name);
    public IEnumerable<string> SectionNames => _sections.Keys;

    // A section name is 8 bytes; "/N" (N decimal) is a byte offset into the COFF
    // string table for names longer than 8 chars (the DWARF .debug_* sections).
    private string SectionName(int hdr, long strTab)
    {
        if (_b[hdr] == (byte)'/')
        {
            int end = hdr + 1;
            while (end < hdr + 8 && _b[end] != 0) end++;
            string digits = System.Text.Encoding.ASCII.GetString(_b, hdr + 1, end - (hdr + 1));
            if (strTab > 0 && int.TryParse(digits, out int off))
                return CStrAt(strTab + off);
            return "";
        }
        int n = 0;
        while (n < 8 && _b[hdr + n] != 0) n++;
        return System.Text.Encoding.ASCII.GetString(_b, hdr, n);
    }

    private string CStrAt(long off)
    {
        if (off < 0 || off >= _b.Length) return "";
        long end = off;
        while (end < _b.Length && _b[end] != 0) end++;
        return System.Text.Encoding.UTF8.GetString(_b, (int)off, (int)(end - off));
    }

    private ushort U16(int o) => (ushort)(_b[o] | (_b[o + 1] << 8));
    private uint U32(int o) => (uint)(_b[o] | (_b[o + 1] << 8) | (_b[o + 2] << 16) | (_b[o + 3] << 24));
}
