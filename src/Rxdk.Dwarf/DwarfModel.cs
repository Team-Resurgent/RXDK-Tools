// SPDX-License-Identifier: GPL-3.0-or-later
// Part of RXDK-Tools - see LICENSE for the full GNU GPL v3.
//
// Target-agnostic DWARF data model + queries. Ported from RXDK-360's
// Rxdk.Xbox360.Dwarf.DwarfModel (unchanged except namespace).

namespace Rxdk.Dwarf;

/// <summary>One row of the line-number table: a guest address and the source
/// position that starts there.</summary>
public sealed class LineRow
{
    public ulong Address;
    public string File = "";
    public int Line;
    public int Column;
    public bool IsStmt;
    public bool EndSequence;
    public override string ToString() =>
        $"0x{Address:X8}  {File}:{Line}" + (Column != 0 ? $":{Column}" : "") +
        (EndSequence ? "  (end)" : IsStmt ? "  (stmt)" : "");
}

/// <summary>A field of a struct / class / union.</summary>
public sealed class DwarfMember
{
    public string Name = "";
    public int Offset;
    public ulong TypeOffset;
}

/// <summary>A DWARF type DIE: size, encoding, and members for Locals expansion.</summary>
public sealed class DwarfType
{
    public ulong Offset;
    public int Tag;
    public string Name = "";
    public int ByteSize;
    public int Encoding;
    public ulong ReferentOffset;
    public int ArrayCount;
    public readonly List<DwarfMember> Members = new();

    public bool IsPointer => Tag == DW_TAG.pointer_type;
    public bool IsArray => Tag == DW_TAG.array_type;
    public bool IsStruct =>
        Tag == DW_TAG.structure_type || Tag == DW_TAG.class_type || Tag == DW_TAG.union_type;
    public bool IsBase => Tag == DW_TAG.base_type;
    public bool IsFloat => Encoding == DW_ATE.float_;
    public bool IsQualifier =>
        Tag == DW_TAG.typedef || Tag == DW_TAG.const_type ||
        Tag == DW_TAG.volatile_type || Tag == DW_TAG.restrict_type;
}

/// <summary>A local variable or parameter of a function.</summary>
public sealed class DwarfVariable
{
    public string Name = "";
    public string TypeName = "";
    public ulong TypeOffset;
    public int DeclLine;
    public bool IsParameter;
    /// <summary>Raw DWARF location expression (e.g. DW_OP_fbreg &lt;offset&gt;).</summary>
    public byte[] Location = System.Array.Empty<byte>();
    public override string ToString() =>
        (IsParameter ? "param " : "local ") + TypeName + " " + Name + LocationSummary.Of(Location);
}

/// <summary>A function: its address range and source position.</summary>
public sealed class DwarfFunction
{
    public string Name = "";
    public ulong LowPc;
    public ulong HighPc;   // exclusive end
    public string File = "";
    public int DeclLine;
    public byte[] FrameBase = System.Array.Empty<byte>();
    public readonly List<DwarfVariable> Variables = new();
    public bool Contains(ulong addr) => addr >= LowPc && addr < HighPc;
    public override string ToString() =>
        $"0x{LowPc:X8}-0x{HighPc:X8}  {Name}  ({File}:{DeclLine})";
}

/// <summary>The call site of an inlined function: the source position of the call (from
/// DW_AT_call_file/line) mapped to the address where the inlined body begins. When a call like
/// <c>Present(...)</c> is inlined, its code carries the callee's own file:line in the line table,
/// so the caller's source line has no row of its own -- this restores that mapping.</summary>
public sealed class InlineSite
{
    public string File = "";
    public int Line;
    public ulong Address;    // low_pc of the inlined body
    public ulong EndAddress; // high_pc (exclusive); 0 if unknown
    public bool Contains(ulong pc) => EndAddress > Address && pc >= Address && pc < EndAddress;
    public ulong Span => EndAddress > Address ? EndAddress - Address : 0;
}

/// <summary>One compilation unit.</summary>
public sealed class CompileUnit
{
    public string Name = "";
    public string CompDir = "";
    public readonly List<DwarfFunction> Functions = new();
    public readonly List<LineRow> Lines = new();
    public readonly List<InlineSite> InlineSites = new();
    public readonly List<DwarfVariable> Globals = new();
}

/// <summary>The parsed DWARF for a title: units, functions and the line table,
/// with the address/source queries a debugger needs.</summary>
public sealed class DwarfInfo
{
    public readonly List<CompileUnit> Units = new();
    public readonly Dictionary<ulong, DwarfType> Types = new();

    public IEnumerable<DwarfFunction> Functions
    {
        get { foreach (var u in Units) foreach (var f in u.Functions) yield return f; }
    }

    /// <summary>The function whose range contains <paramref name="addr"/>, or null.</summary>
    public DwarfFunction? FunctionAt(ulong addr)
    {
        foreach (var f in Functions)
            if (f.LowPc != 0 && f.Contains(addr)) return f;
        return null;
    }

    /// <summary>The source position for a guest address (the last line row at or
    /// before it within its sequence), or null.</summary>
    public LineRow? LineAt(ulong addr)
    {
        LineRow? best = null;
        foreach (var u in Units)
        {
            for (int i = 0; i < u.Lines.Count; i++)
            {
                var r = u.Lines[i];
                if (r.EndSequence) continue;
                // A row covers [r.Address, nextRow.Address).
                ulong end = (i + 1 < u.Lines.Count) ? u.Lines[i + 1].Address : r.Address + 1;
                if (addr >= r.Address && addr < end)
                    if (best == null || r.Address >= best.Address) best = r;
            }
        }
        return best;
    }

    /// <summary>Follow typedef / const / volatile / restrict to the underlying type.</summary>
    public DwarfType? Peel(DwarfType? type)
    {
        int n = 0;
        while (type != null && type.IsQualifier && type.ReferentOffset != 0 && n++ < 16)
        {
            if (!Types.TryGetValue(type.ReferentOffset, out var next))
                break;
            type = next;
        }
        return type;
    }

    public DwarfType? TypeOf(ulong offset)
    {
        if (offset == 0 || !Types.TryGetValue(offset, out var t))
            return null;
        return Peel(t);
    }

    public int SizeOf(ulong typeOffset)
    {
        if (typeOffset != 0 && Types.TryGetValue(typeOffset, out var raw))
            return SizeOf(raw);
        return 4;
    }

    public int SizeOf(DwarfType? type)
    {
        type = Peel(type);
        if (type == null)
            return 4;
        if (type.ByteSize > 0)
            return type.ByteSize;
        if (type.IsPointer)
            return 4;
        if (type.IsArray && type.ReferentOffset != 0)
        {
            int elem = SizeOf(type.ReferentOffset);
            return type.ArrayCount > 0 ? elem * type.ArrayCount : elem;
        }
        return 4;
    }

    public string DisplayName(ulong typeOffset)
    {
        if (typeOffset == 0 || !Types.TryGetValue(typeOffset, out var t))
            return "";
        return DisplayName(t, 0);
    }

    private string DisplayName(DwarfType t, int depth)
    {
        if (depth > 16)
            return "";
        if (t.IsPointer)
            return (t.ReferentOffset != 0 && Types.TryGetValue(t.ReferentOffset, out var pointee)
                ? DisplayName(pointee, depth + 1) : "void") + "*";
        if (t.Tag == DW_TAG.const_type)
            return "const " + (t.ReferentOffset != 0 && Types.TryGetValue(t.ReferentOffset, out var c)
                ? DisplayName(c, depth + 1) : "void");
        if (t.IsArray)
        {
            string elem = t.ReferentOffset != 0 && Types.TryGetValue(t.ReferentOffset, out var e)
                ? DisplayName(e, depth + 1) : "";
            return t.ArrayCount > 0 ? $"{elem}[{t.ArrayCount}]" : elem + "[]";
        }
        if (!string.IsNullOrEmpty(t.Name))
            return t.IsStruct && t.Tag == DW_TAG.structure_type ? "struct " + t.Name : t.Name;
        if (t.IsQualifier && t.ReferentOffset != 0 && Types.TryGetValue(t.ReferentOffset, out var q))
            return DisplayName(q, depth + 1);
        return t.IsStruct ? "{struct}" : "";
    }

    public bool IsExpandable(DwarfType? type)
    {
        type = Peel(type);
        if (type == null)
            return false;
        if (type.IsPointer)
        {
            var r = TypeOf(type.ReferentOffset);
            return r != null && (r.IsStruct || r.IsArray || r.IsPointer);
        }
        if (type.IsArray)
            return type.ArrayCount > 0 || type.ReferentOffset != 0;
        return type.Members.Count > 0;
    }

    public IEnumerable<InlineSite> InlineSites
    {
        get { foreach (var u in Units) foreach (var s in u.InlineSites) yield return s; }
    }

    /// <summary>File-scope (statically-addressed) globals across all units.</summary>
    public IEnumerable<DwarfVariable> Globals
    {
        get { foreach (var u in Units) foreach (var g in u.Globals) yield return g; }
    }

    /// <summary>True if a variable of this type is const (a read-only lookup table rather than
    /// mutable program state). Walks typedef/volatile/restrict and into array elements.</summary>
    public bool IsConstType(ulong typeOffset) => ConstWalk(typeOffset, 0);

    private bool ConstWalk(ulong offset, int depth)
    {
        if (offset == 0 || depth > 16 || !Types.TryGetValue(offset, out var t)) return false;
        if (t.Tag == DW_TAG.const_type) return true;
        if (t.Tag == DW_TAG.typedef || t.Tag == DW_TAG.volatile_type || t.Tag == DW_TAG.restrict_type)
            return ConstWalk(t.ReferentOffset, depth + 1);
        if (t.IsArray) return ConstWalk(t.ReferentOffset, depth + 1);
        return false;
    }

    /// <summary>The lowest address mapped to a given file:line (for setting a breakpoint), or null.
    /// Considers both the line table and inline call sites -- so a line whose call was inlined (its
    /// code tagged to the callee's header) still binds, to where the inlined body begins.</summary>
    public ulong? AddressFor(string file, int line)
    {
        ulong? best = null;
        void Take(string rowFile, int rowLine, ulong addr, bool end)
        {
            if (end || rowLine != line) return;
            if (!FileMatch(file, rowFile)) return;
            if (best == null || addr < best) best = addr;
        }
        foreach (var u in Units)
        {
            foreach (var r in u.Lines) Take(r.File, r.Line, r.Address, r.EndSequence);
            foreach (var s in u.InlineSites) Take(s.File, s.Line, s.Address, false);
        }
        return best;
    }

    private static bool FileMatch(string want, string have) =>
        want.Equals(have, System.StringComparison.OrdinalIgnoreCase) ||
        have.EndsWith(want, System.StringComparison.OrdinalIgnoreCase) ||
        want.EndsWith(have, System.StringComparison.OrdinalIgnoreCase);
}

internal static class LocationSummary
{
    // A short human-readable decode of the common location forms, for dumps.
    public static string Of(byte[] loc)
    {
        if (loc.Length == 0) return "";
        byte op = loc[0];
        // DW_OP_fbreg = 0x91 (SLEB offset from the frame base)
        if (op == 0x91 && loc.Length > 1)
        {
            var c = new ByteCursor(loc, pos: 1);
            return $"  [fbreg {c.SLeb()}]";
        }
        // DW_OP_addr = 0x03 (a fixed, target-endian address)
        if (op == 0x03 && loc.Length >= 5)
        {
            var c = new ByteCursor(loc, pos: 1);
            return $"  [addr 0x{c.U32():X8}]";
        }
        // DW_OP_reg0..31 = 0x50..0x6f
        if (op >= 0x50 && op <= 0x6f) return $"  [reg {op - 0x50}]";
        // DW_OP_breg0..31 = 0x70..0x8f (SLEB offset)
        if (op >= 0x70 && op <= 0x8f && loc.Length > 1)
        {
            var c = new ByteCursor(loc, pos: 1);
            return $"  [breg{op - 0x70} {c.SLeb()}]";
        }
        return $"  [op 0x{op:X2}]";
    }
}
