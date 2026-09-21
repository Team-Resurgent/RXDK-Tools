using Rxdk.Dwarf;
using Rxdk.Xbdm;

namespace Rxdk.XboxDbgBridge.Symbols;

/// <summary>
/// Symbol resolution backed by the DWARF the RXDK clang build emits into the title's .exe, the
/// replacement for the MSVC-.pdb path (clang no longer writes a .pdb). Ported from RXDK-360's
/// DwarfValues/Symbols and adapted to the original Xbox: x86 register context (<see cref="XbdmContext"/>),
/// kit memory via <see cref="KitMemoryAccess"/>, and the bridge's <see cref="VariableJson"/> shape.
/// The line/type reader lives in Rxdk.Dwarf; this class does the live-frame work: frame-base + location
/// evaluation against the current registers, then formats + expands values from kit memory.
/// </summary>
internal sealed class DwarfSymbols
{
    private readonly DwarfInfo _info;
    private readonly nuint _moduleBase;   // kit runtime base of the module
    private readonly ulong _imageBase;    // DWARF/link base the addresses are relative to (RXDK: 0x10000)

    internal DwarfSymbols(DwarfInfo info, nuint moduleBase, ulong imageBase)
    {
        _info = info;
        _moduleBase = moduleBase;
        _imageBase = imageBase;
    }

    // A kit runtime address <-> the DWARF virtual address (identity when the title loads at its link
    // base, which OG titles do; the relocation keeps it correct if the kit ever reports a different base).
    private ulong ToVa(uint kit) => _moduleBase == 0 ? kit : _imageBase + (kit - (ulong)_moduleBase);

    // ---- location evaluation (x86) ---------------------------------------

    // DWARF register number -> x86 register value (SysV i386 numbering).
    private static uint X86Reg(ref XbdmContext c, int dw) => dw switch
    {
        0 => c.Eax, 1 => c.Ecx, 2 => c.Edx, 3 => c.Ebx,
        4 => c.Esp, 5 => c.Ebp, 6 => c.Esi, 7 => c.Edi, 8 => c.Eip,
        _ => 0,
    };

    private static ulong EvalFrameBase(byte[] loc, ref XbdmContext c)
    {
        if (loc.Length == 0) return c.Ebp;
        byte op = loc[0];
        if (op >= 0x50 && op <= 0x6f) return X86Reg(ref c, op - 0x50);        // DW_OP_reg0..31
        if (op == 0x9c) return (ulong)c.Ebp + 8;                             // DW_OP_call_frame_cfa (frame-ptr CFA)
        if (op >= 0x70 && op <= 0x8f)                                        // DW_OP_breg0..31
            return (ulong)((long)X86Reg(ref c, op - 0x70) + Sleb(loc, 1));
        return c.Ebp;
    }

    private static (uint? addr, int? reg) EvalLocation(byte[] loc, ulong frameBase, ref XbdmContext c)
    {
        if (loc.Length == 0) return (null, null);
        byte op = loc[0];
        if (op == 0x91)                                                      // DW_OP_fbreg <sleb>
            return ((uint)((long)frameBase + Sleb(loc, 1)), null);
        if (op == 0x03 && loc.Length >= 5)                                  // DW_OP_addr <u32 LE>
            return ((uint)(loc[1] | (loc[2] << 8) | (loc[3] << 16) | (loc[4] << 24)), null);
        if (op >= 0x50 && op <= 0x6f) return (null, op - 0x50);              // DW_OP_reg0..31
        if (op >= 0x70 && op <= 0x8f)                                        // DW_OP_breg0..31 <sleb>
            return ((uint)((long)X86Reg(ref c, op - 0x70) + Sleb(loc, 1)), null);
        return (null, null);
    }

    private static long Sleb(byte[] b, int pos)
    {
        long r = 0; int s = 0; byte x;
        do { x = b[pos++]; r |= (long)(x & 0x7f) << s; s += 7; } while ((x & 0x80) != 0);
        if (s < 64 && (x & 0x40) != 0) r |= -1L << s;
        return r;
    }

    // ---- kit memory helpers (compose the sizes DWARF needs from ReadDword) ----

    private static uint? ReadDword(KitMemoryAccess m, uint addr) => m.ReadDword((nuint)addr);

    private static ulong? ReadQword(KitMemoryAccess m, uint addr)
    {
        var lo = m.ReadDword((nuint)addr);
        var hi = m.ReadDword((nuint)(addr + 4));
        return lo is null || hi is null ? null : ((ulong)hi.Value << 32) | lo.Value;
    }

    private static ulong? ReadSized(KitMemoryAccess m, uint addr, int size)
    {
        if (size >= 8) return ReadQword(m, addr);
        var dw = m.ReadDword((nuint)addr);
        if (dw is null) return null;
        return size switch { 1 => dw.Value & 0xFF, 2 => dw.Value & 0xFFFF, _ => dw.Value };
    }

    // ---- public surface used by SymbolService ----------------------------

    internal bool TryAddressToLine(uint kitAddress, out string file, out uint line, out string function)
    {
        file = string.Empty; line = 0; function = string.Empty;
        ulong va = ToVa(kitAddress);
        var fn = _info.FunctionAt(va);
        if (fn != null) function = fn.Name;
        var ln = _info.LineAt(va);
        if (ln == null) return false;
        file = ln.File; line = (uint)ln.Line;
        return true;
    }

    internal bool TryResolveLine(string file, uint line, out uint kitAddress)
    {
        kitAddress = 0;
        string want = Norm(file);
        string wantBase = BaseName(want);

        // Exact line first (lowest address among matching rows), then the nearest statement at or
        // after it -- VS often plants a breakpoint on a brace/blank line with no row of its own.
        ulong? exact = null;
        int fbLine = int.MaxValue; ulong fbAddr = 0; bool fb = false;
        foreach (var u in _info.Units)
            foreach (var r in u.Lines)
            {
                if (r.EndSequence || !FileMatch(want, wantBase, r.File)) continue;
                if (r.Line == (int)line)
                {
                    if (exact is null || r.Address < exact) exact = r.Address;
                }
                else if (r.Line > (int)line && r.Line < fbLine)
                {
                    fbLine = r.Line; fbAddr = r.Address; fb = true;
                }
            }

        ulong? va = exact ?? (fb ? fbAddr : (ulong?)null);
        if (va is null) return false;
        kitAddress = (uint)ToKit(va.Value);
        return true;
    }

    private nuint ToKit(ulong va) => _moduleBase == 0 ? (nuint)va : (nuint)(_moduleBase + (nuint)(va - _imageBase));

    private static string Norm(string p) => (p ?? "").Replace('/', '\\');
    private static string BaseName(string p)
    {
        int i = p.LastIndexOf('\\');
        return i >= 0 ? p[(i + 1)..] : p;
    }
    private static bool FileMatch(string want, string wantBase, string have)
    {
        have = Norm(have);
        return want.Equals(have, StringComparison.OrdinalIgnoreCase)
            || have.EndsWith(want, StringComparison.OrdinalIgnoreCase)
            || want.EndsWith(have, StringComparison.OrdinalIgnoreCase)
            || BaseName(have).Equals(wantBase, StringComparison.OrdinalIgnoreCase);
    }

    internal bool EmitLocals(ref XbdmContext context, VariableJson variables, KitMemoryAccess memory)
    {
        var fn = _info.FunctionAt(ToVa(context.Eip));
        if (fn == null) return false;
        ulong frameBase = EvalFrameBase(fn.FrameBase, ref context);
        bool any = false;
        foreach (var v in fn.Variables)
        {
            if (variables.IsFull) break;
            if (IsHidden(v.Name) || variables.WasEmitted(v.Name)) continue;
            var (addr, reg) = EvalLocation(v.Location, frameBase, ref context);
            EmitSlot(v, addr, reg, ref context, memory, variables);
            any = true;
        }
        return any;
    }

    internal bool TryEvaluate(string expression, ref XbdmContext context, KitMemoryAccess memory,
                              out string value, out string? error, out bool expandable)
    {
        value = string.Empty; error = null; expandable = false;
        var fn = _info.FunctionAt(ToVa(context.Eip));
        if (fn == null) { error = "noFrame"; return false; }
        ulong frameBase = EvalFrameBase(fn.FrameBase, ref context);
        foreach (var v in fn.Variables)
        {
            if (!string.Equals(v.Name, expression, StringComparison.Ordinal) &&
                !string.Equals(v.Name, expression, StringComparison.OrdinalIgnoreCase))
                continue;
            var (addr, reg) = EvalLocation(v.Location, frameBase, ref context);
            value = DescribeSlot(v, addr, reg, ref context, memory, out expandable, out _);
            return true;
        }
        error = "symbolNotFound";
        return false;
    }

    internal bool TryEmitMembers(string expandKey, ref XbdmContext context, VariableJson variables, KitMemoryAccess memory)
    {
        // Synthetic children carry a self-contained "@<hexaddr>#<hextypeoffset>" key.
        if (TryParseRef(expandKey, out uint refAddr, out ulong refType))
        {
            EmitChildren(refType, refAddr, memory, variables);
            return variables.Count > 0;
        }
        // Otherwise the key is a top-level local name: resolve it in the current frame, then expand.
        var fn = _info.FunctionAt(ToVa(context.Eip));
        if (fn == null) return false;
        ulong frameBase = EvalFrameBase(fn.FrameBase, ref context);
        foreach (var v in fn.Variables)
        {
            if (!string.Equals(v.Name, expandKey, StringComparison.Ordinal)) continue;
            var (addr, _) = EvalLocation(v.Location, frameBase, ref context);
            if (addr is not uint a) return false;
            EmitChildren(v.TypeOffset, a, memory, variables);
            return variables.Count > 0;
        }
        return false;
    }

    // ---- value formatting / expansion (ported from RXDK-360 DwarfValues) ----

    private void EmitSlot(DwarfVariable v, uint? addr, int? reg, ref XbdmContext ctx, KitMemoryAccess memory, VariableJson variables)
    {
        string value = DescribeSlot(v, addr, reg, ref ctx, memory, out bool expandable, out string key);
        variables.Append(v.Name, value, expandable, key);
    }

    private string DescribeSlot(DwarfVariable v, uint? addr, int? reg, ref XbdmContext ctx, KitMemoryAccess memory,
                                out bool expandable, out string expandKey)
    {
        expandable = false; expandKey = "";
        var type = v.TypeOffset != 0 ? _info.TypeOf(v.TypeOffset) : null;
        if (addr is uint a)
        {
            expandable = _info.IsExpandable(type);
            if (expandable) expandKey = AddrRef(a, v.TypeOffset);
            return Describe(v.TypeOffset, a, memory, v.TypeName);
        }
        if (reg is int r)
            return FormatRaw(type, v.TypeName, X86Reg(ref ctx, r), Info(v.TypeOffset, v.TypeName));
        return "<optimized out>";
    }

    private int Info(ulong typeOffset, string typeName) =>
        typeOffset != 0 ? _info.SizeOf(typeOffset) : SizeOfName(typeName);

    private void EmitChildren(ulong typeOffset, uint address, KitMemoryAccess memory, VariableJson variables)
    {
        var type = _info.TypeOf(typeOffset);
        if (type == null || variables.IsFull) return;

        if (type.IsPointer)
        {
            var ptr = ReadDword(memory, address);
            if (ptr is null or 0) return;
            EmitChildren(type.ReferentOffset, ptr.Value, memory, variables);
            return;
        }

        if (type.IsArray)
        {
            int count = type.ArrayCount > 0 ? type.ArrayCount : 0;
            if (count <= 0) return;
            if (count > 256) count = 256;
            int elemSize = _info.SizeOf(type.ReferentOffset);
            if (elemSize <= 0) elemSize = 4;
            for (int i = 0; i < count && !variables.IsFull; i++)
            {
                uint elemAddr = address + (uint)(i * elemSize);
                bool exp = _info.IsExpandable(_info.TypeOf(type.ReferentOffset));
                variables.Append($"[{i}]",
                    Describe(type.ReferentOffset, elemAddr, memory, _info.DisplayName(type.ReferentOffset)),
                    exp, exp ? AddrRef(elemAddr, type.ReferentOffset) : "");
            }
            return;
        }

        foreach (var m in type.Members)
        {
            if (variables.IsFull) return;
            uint memberAddr = address + (uint)m.Offset;
            var mt = _info.TypeOf(m.TypeOffset);
            if (string.IsNullOrEmpty(m.Name) && mt != null && (mt.IsStruct || mt.IsArray))
            {
                EmitChildren(m.TypeOffset, memberAddr, memory, variables);   // flatten base classes
                continue;
            }
            if (string.IsNullOrEmpty(m.Name)) continue;
            bool exp = _info.IsExpandable(mt);
            variables.Append(m.Name,
                Describe(m.TypeOffset, memberAddr, memory, _info.DisplayName(m.TypeOffset)),
                exp, exp ? AddrRef(memberAddr, m.TypeOffset) : "");
        }
    }

    private string Describe(ulong typeOffset, uint address, KitMemoryAccess memory, string fallbackName)
    {
        var type = _info.TypeOf(typeOffset);
        if (type == null)
            return FormatNamed(fallbackName, ReadSized(memory, address, 4) ?? 0, 4);

        if (type.IsPointer)
        {
            var ptr = ReadDword(memory, address);
            return ptr is null ? "<unreadable>" : $"0x{ptr.Value:x8}";
        }

        if (type.IsArray || type.IsStruct)
        {
            int size = _info.SizeOf(type);
            string name = !string.IsNullOrEmpty(fallbackName) ? fallbackName : _info.DisplayName(typeOffset);
            return string.IsNullOrEmpty(name) ? $"{{{size} bytes}}" : name;
        }

        if (type.IsFloat && type.ByteSize == 8)
        {
            var bits = ReadQword(memory, address);
            return bits is null ? "<unreadable>" : $"{BitConverter.Int64BitsToDouble((long)bits.Value):g}";
        }
        if (type.IsFloat)
        {
            var bits = ReadDword(memory, address);
            return bits is null ? "<unreadable>" : $"{BitConverter.Int32BitsToSingle((int)bits.Value):g}";
        }

        int sz = _info.SizeOf(type);
        if (sz == 8)
        {
            var q = ReadQword(memory, address);
            return q is null ? "<unreadable>" : ((long)q.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var raw = ReadSized(memory, address, sz <= 0 ? 4 : sz);
        if (raw is null) return "<unreadable>";
        return FormatNamed(!string.IsNullOrEmpty(fallbackName) ? fallbackName : _info.DisplayName(typeOffset), raw.Value, sz);
    }

    private static string FormatRaw(DwarfType? type, string typeName, ulong raw, int size)
    {
        if (type != null && type.IsFloat && size == 8) return $"{BitConverter.Int64BitsToDouble((long)raw):g}";
        if (type != null && type.IsFloat) return $"{BitConverter.Int32BitsToSingle((int)raw):g}";
        return FormatNamed(typeName, raw, size);
    }

    private static string FormatNamed(string type, ulong raw, int size)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string t = (type ?? "").Replace("const ", "").Trim();
        if (t.EndsWith("*", StringComparison.Ordinal)) return $"0x{raw:x8}";
        if (t is "float") return $"{BitConverter.Int32BitsToSingle((int)raw):g}";
        if (t is "double") return $"{BitConverter.Int64BitsToDouble((long)raw):g}";
        if (t is "char" or "signed char" or "unsigned char")
        {
            char ch = (char)(raw & 0xff);
            return char.IsControl(ch) ? ((long)raw).ToString(ci) : $"'{ch}' ({(long)raw})";
        }
        if (t is "_Bool" or "bool") return raw != 0 ? "true" : "false";
        if (t.Contains("unsigned")) return $"{raw} (0x{raw:x})";
        long sv = size switch { 1 => (sbyte)raw, 2 => (short)raw, 4 => (int)raw, _ => (long)raw };
        return $"{sv.ToString(ci)} (0x{raw:x})";
    }

    private static int SizeOfName(string type)
    {
        string t = (type ?? "").Replace("const ", "").Trim();
        if (t.EndsWith("*", StringComparison.Ordinal)) return 4;
        return t switch
        {
            "char" or "signed char" or "unsigned char" or "_Bool" or "bool" => 1,
            "short" or "short int" or "unsigned short" => 2,
            "long long" or "long long int" or "unsigned long long" or "double" => 8,
            _ => 4,
        };
    }

    private static bool IsHidden(string name) =>
        string.IsNullOrEmpty(name) || name.StartsWith("__", StringComparison.Ordinal);

    // A self-contained expand key: "@<hexaddr>#<hextypeoffset>" (matches the bridge's '@' convention).
    private static string AddrRef(uint address, ulong typeOffset) => $"@{address:x}#{typeOffset:x}";

    private static bool TryParseRef(string s, out uint address, out ulong typeOffset)
    {
        address = 0; typeOffset = 0;
        if (string.IsNullOrEmpty(s) || s[0] != '@') return false;
        int hash = s.IndexOf('#');
        if (hash < 2) return false;
        try
        {
            address = (uint)Convert.ToUInt64(s.Substring(1, hash - 1), 16);
            typeOffset = Convert.ToUInt64(s.Substring(hash + 1), 16);
            return true;
        }
        catch { return false; }
    }
}
