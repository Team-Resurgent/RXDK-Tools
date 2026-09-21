// SPDX-License-Identifier: GPL-3.0-or-later
// Part of RXDK-Tools - see LICENSE for the full GNU GPL v3.
//
// A DWARF v4/v5 reader for original-Xbox titles: it turns the .debug_* sections
// of a linked i686 PE into functions, a line table and locals - the symbol
// backbone the devkit debugger maps onto the running title. Ported from
// RXDK-360's DwarfReader; the parsing is target-agnostic, so the OG changes are
// only (1) reading through an endian-aware ByteCursor (x86 = little-endian) and
// (2) taking an ISectionSource (PeImage) instead of an ELF. Scope is what a
// stepping debugger needs (line program + subprograms + variables + basic
// types), not a full DWARF consumer.

namespace Rxdk.Dwarf;

public sealed class DwarfReader
{
    private sealed class Abbrev
    {
        public int Tag;
        public bool HasChildren;
        public readonly List<(int attr, int form, long implicit_)> Attrs = new();
    }

    private sealed class Die
    {
        public int Tag;
        public ulong Offset;                 // absolute offset in .debug_info
        public readonly Dictionary<int, object> Attrs = new();
        public readonly Dictionary<int, int> Forms = new();
        public readonly List<Die> Children = new();

        public string Str(int at) => Attrs.TryGetValue(at, out var v) ? v as string ?? "" : "";
        public bool Has(int at) => Attrs.ContainsKey(at);
        public ulong U(int at) => Attrs.TryGetValue(at, out var v) && v is ulong u ? u : 0;
        public byte[] Blk(int at) => Attrs.TryGetValue(at, out var v) ? v as byte[] ?? Array.Empty<byte>() : Array.Empty<byte>();
    }

    private readonly byte[] _info, _abbrev, _str, _lineStr, _line;
    private readonly bool _be;
    private readonly Dictionary<ulong, Die> _byOffset = new();

    private DwarfReader(ISectionSource src)
    {
        _be = src.IsBigEndian;
        _info = src.Section(".debug_info");
        _abbrev = src.Section(".debug_abbrev");
        _str = src.Section(".debug_str");
        _lineStr = src.Section(".debug_line_str");
        _line = src.Section(".debug_line");
    }

    public static DwarfInfo Read(string imagePath) => Read(PeImage.Load(imagePath));

    public static DwarfInfo Read(ISectionSource src)
    {
        var r = new DwarfReader(src);
        var info = new DwarfInfo();
        if (r._info.Length == 0) return info;
        var cur = new ByteCursor(r._info, r._be);
        while (cur.Pos + 11 <= r._info.Length)
        {
            var cu = r.ReadCompileUnit(cur);
            if (cu != null) info.Units.Add(cu);
        }
        r.AttachTypes(info);
        return info;
    }

    private CompileUnit? ReadCompileUnit(ByteCursor cur)
    {
        int cuStart = cur.Pos;
        uint unitLength = cur.U32();
        if (unitLength == 0 || unitLength >= 0xfffffff0u) return null;   // 64-bit DWARF not emitted
        int next = cur.Pos + (int)unitLength;
        ushort version = cur.U16();
        uint abbrevOff; int addrSize;
        if (version >= 5)
        {
            cur.U8();                       // unit_type
            addrSize = cur.U8();
            abbrevOff = cur.U32();
        }
        else
        {
            abbrevOff = cur.U32();
            addrSize = cur.U8();
        }

        var abbrevs = ParseAbbrev(abbrevOff);
        var root = ReadDie(cur, abbrevs, cuStart, addrSize, next);
        cur.Pos = next;                     // resume at the next CU regardless
        if (root == null || root.Tag != DW_TAG.compile_unit) return null;

        var unit = new CompileUnit { Name = root.Str(DW_AT.name), CompDir = root.Str(DW_AT.comp_dir) };

        // Line table (address <-> source).
        List<string> files = new();
        if (root.Has(DW_AT.stmt_list))
            ParseLineProgram((int)root.U(DW_AT.stmt_list), addrSize, unit, files);

        // Functions + their variables.
        foreach (var die in Flatten(root))
        {
            if (die.Tag != DW_TAG.subprogram || !die.Has(DW_AT.low_pc)) continue;
            ulong low = die.U(DW_AT.low_pc);
            ulong high = die.U(DW_AT.high_pc);
            // high_pc is an address if its form is DW_FORM_addr, else an offset.
            if (die.Forms.TryGetValue(DW_AT.high_pc, out int hf) && hf != DW_FORM.addr) high = low + high;
            var fn = new DwarfFunction
            {
                Name = die.Str(DW_AT.name),
                LowPc = low,
                HighPc = high,
                DeclLine = (int)die.U(DW_AT.decl_line),
                FrameBase = die.Blk(DW_AT.frame_base),
                File = FileName(files, (int)die.U(DW_AT.decl_file)),
            };
            CollectVariables(die, fn, files);
            unit.Functions.Add(fn);
        }

        // Inlined call sites: an inlined call (e.g. an inlined Present) leaves the caller's source
        // line with no line-table row -- the inlined body is tagged to the callee's file. Record
        // DW_AT_call_file/line -> the inlined body's low_pc so that caller line still binds.
        foreach (var die in Flatten(root))
        {
            if (die.Tag != DW_TAG.inlined_subroutine || !die.Has(DW_AT.low_pc) || !die.Has(DW_AT.call_line))
                continue;
            ulong ilow = die.U(DW_AT.low_pc);
            ulong ihigh = die.U(DW_AT.high_pc);
            // high_pc is an address if its form is DW_FORM_addr, else an offset from low_pc.
            if (die.Has(DW_AT.high_pc) && die.Forms.TryGetValue(DW_AT.high_pc, out int ihf) && ihf != DW_FORM.addr)
                ihigh = ilow + ihigh;
            unit.InlineSites.Add(new InlineSite
            {
                Address = ilow,
                EndAddress = die.Has(DW_AT.high_pc) ? ihigh : 0,
                Line = (int)die.U(DW_AT.call_line),
                File = FileName(files, (int)die.U(DW_AT.call_file)),
            });
        }

        // File-scope globals: DW_TAG_variable with a static address (DW_OP_addr). These are the
        // program's globals the Globals pane shows. Descend through namespaces (so anonymous-namespace
        // globals like a `static` file-scope pointer are found) but not into functions -- function
        // statics are not module globals. Skip extern declarations and register/stack forms.
        CollectGlobals(root, unit, files);
        return unit;
    }

    private void CollectGlobals(Die scope, CompileUnit unit, List<string> files)
    {
        foreach (var child in scope.Children)
        {
            if (child.Tag == DW_TAG.@namespace)
            {
                CollectGlobals(child, unit, files);   // globals nested in a (possibly anonymous) namespace
                continue;
            }
            if (child.Tag != DW_TAG.variable) continue;
            byte[] loc = child.Has(DW_AT.location) ? child.Blk(DW_AT.location) : System.Array.Empty<byte>();
            if (loc.Length < 5 || loc[0] != 0x03) continue;   // DW_OP_addr <u32>
            Die named = child;
            if (child.Has(DW_AT.specification) && _byOffset.TryGetValue(child.U(DW_AT.specification), out var spec))
                named = spec;
            string name = child.Has(DW_AT.name) ? child.Str(DW_AT.name)
                : named.Has(DW_AT.name) ? named.Str(DW_AT.name) : "";
            if (string.IsNullOrEmpty(name)) continue;
            ulong typeOff = child.Has(DW_AT.type) ? child.U(DW_AT.type)
                : named.Has(DW_AT.type) ? named.U(DW_AT.type) : 0;
            unit.Globals.Add(new DwarfVariable
            {
                Name = name,
                Location = loc,
                TypeOffset = typeOff,
                TypeName = typeOff != 0 ? TypeName(typeOff, 0) : "",
                DeclLine = (int)(child.Has(DW_AT.decl_line) ? child.U(DW_AT.decl_line)
                    : named.Has(DW_AT.decl_line) ? named.U(DW_AT.decl_line) : 0),
            });
        }
    }

    private void CollectVariables(Die scope, DwarfFunction fn, List<string> files)
    {
        foreach (var child in scope.Children)
        {
            if (child.Tag == DW_TAG.variable || child.Tag == DW_TAG.formal_parameter)
            {
                Die named = child;
                if (child.Has(DW_AT.specification) &&
                    _byOffset.TryGetValue(child.U(DW_AT.specification), out var spec))
                    named = spec;
                ulong typeOff = child.Has(DW_AT.type) ? child.U(DW_AT.type)
                    : named.Has(DW_AT.type) ? named.U(DW_AT.type) : 0;
                string name = child.Has(DW_AT.name) ? child.Str(DW_AT.name) : named.Str(DW_AT.name);
                byte[] loc = child.Has(DW_AT.location) ? child.Blk(DW_AT.location)
                    : named.Blk(DW_AT.location);
                fn.Variables.Add(new DwarfVariable
                {
                    Name = name,
                    IsParameter = child.Tag == DW_TAG.formal_parameter,
                    DeclLine = (int)(child.Has(DW_AT.decl_line) ? child.U(DW_AT.decl_line) : named.U(DW_AT.decl_line)),
                    Location = loc,
                    TypeOffset = typeOff,
                    TypeName = typeOff != 0 ? TypeName(typeOff, 0) : "",
                });
            }
            else if (child.Tag == DW_TAG.lexical_block)
            {
                CollectVariables(child, fn, files);   // locals in nested scopes
            }
        }
    }

    private void AttachTypes(DwarfInfo info)
    {
        foreach (var kv in _byOffset)
        {
            var die = kv.Value;
            var t = new DwarfType
            {
                Offset = kv.Key,
                Tag = die.Tag,
                Name = die.Str(DW_AT.name),
                ByteSize = die.Has(DW_AT.byte_size) ? (int)die.U(DW_AT.byte_size) : 0,
                Encoding = die.Has(DW_AT.encoding) ? (int)die.U(DW_AT.encoding) : 0,
                ReferentOffset = die.Has(DW_AT.type) ? die.U(DW_AT.type) : 0,
            };
            if (die.Tag == DW_TAG.array_type)
                t.ArrayCount = ArrayBound(die);
            info.Types[kv.Key] = t;
        }
        foreach (var kv in _byOffset)
        {
            if (kv.Value.Tag != DW_TAG.structure_type &&
                kv.Value.Tag != DW_TAG.class_type &&
                kv.Value.Tag != DW_TAG.union_type)
                continue;
            FillMembers(info.Types[kv.Key], kv.Value);
        }
    }

    private static int ArrayBound(Die die)
    {
        int n = 1;
        bool any = false;
        foreach (var child in die.Children)
        {
            if (child.Tag != DW_TAG.subrange_type) continue;
            any = true;
            if (child.Has(DW_AT.count))
                n *= (int)child.U(DW_AT.count);
            else if (child.Has(DW_AT.upper_bound))
                n *= (int)child.U(DW_AT.upper_bound) + 1;
        }
        return any ? n : 0;
    }

    private static void FillMembers(DwarfType type, Die die)
    {
        foreach (var child in die.Children)
        {
            if (child.Tag != DW_TAG.member && child.Tag != DW_TAG.inheritance)
                continue;
            type.Members.Add(new DwarfMember
            {
                Name = child.Tag == DW_TAG.inheritance ? "" : child.Str(DW_AT.name),
                Offset = MemberOffset(child),
                TypeOffset = child.Has(DW_AT.type) ? child.U(DW_AT.type) : 0,
            });
        }
    }

    private static int MemberOffset(Die die)
    {
        if (!die.Has(DW_AT.data_member_location) ||
            !die.Attrs.TryGetValue(DW_AT.data_member_location, out var v))
            return 0;
        if (v is ulong u) return (int)u;
        if (v is byte[] b) return DecodeMemberLoc(b);
        return 0;
    }

    private static int DecodeMemberLoc(byte[] loc)
    {
        if (loc.Length == 0) return 0;
        var c = new ByteCursor(loc);   // member-location ops are LEB/byte, endian-independent
        byte op = c.U8();
        if (op == DW_OP.plus_uconst) return (int)c.ULeb();
        if (op >= DW_OP.lit0 && op <= DW_OP.lit31) return op - DW_OP.lit0;
        if (op == DW_OP.constu) return (int)c.ULeb();
        if (op == DW_OP.consts) return (int)c.SLeb();
        return 0;
    }

    private string TypeName(ulong offset, int depth)
    {
        if (depth > 16 || !_byOffset.TryGetValue(offset, out var t)) return "";
        switch (t.Tag)
        {
            case DW_TAG.base_type:
            case DW_TAG.typedef:
                return t.Str(DW_AT.name);
            case DW_TAG.pointer_type:
                return (t.Has(DW_AT.type) ? TypeName(t.U(DW_AT.type), depth + 1) : "void") + "*";
            case DW_TAG.const_type:
                return "const " + (t.Has(DW_AT.type) ? TypeName(t.U(DW_AT.type), depth + 1) : "void");
            case DW_TAG.structure_type:
            case DW_TAG.class_type:
                return "struct " + t.Str(DW_AT.name);
            case DW_TAG.union_type:
                return "union " + t.Str(DW_AT.name);
            case DW_TAG.array_type:
                return (t.Has(DW_AT.type) ? TypeName(t.U(DW_AT.type), depth + 1) : "") + "[]";
            default:
                return t.Str(DW_AT.name);
        }
    }

    private static IEnumerable<Die> Flatten(Die d)
    {
        yield return d;
        foreach (var c in d.Children)
            foreach (var g in Flatten(c))
                yield return g;
    }

    // ---- DIE / abbrev -----------------------------------------------------

    private Dictionary<ulong, Abbrev> ParseAbbrev(uint offset)
    {
        var map = new Dictionary<ulong, Abbrev>();
        if (offset >= _abbrev.Length) return map;
        var c = new ByteCursor(_abbrev, _be, (int)offset);
        while (!c.AtEnd)
        {
            ulong code = c.ULeb();
            if (code == 0) break;
            var a = new Abbrev { Tag = (int)c.ULeb(), HasChildren = c.U8() != 0 };
            while (true)
            {
                int at = (int)c.ULeb();
                int form = (int)c.ULeb();
                long impl = 0;
                if (form == DW_FORM.implicit_const) impl = c.SLeb();
                if (at == 0 && form == 0) break;
                a.Attrs.Add((at, form, impl));
            }
            map[code] = a;
        }
        return map;
    }

    private Die? ReadDie(ByteCursor cur, Dictionary<ulong, Abbrev> abbrevs,
                         int cuStart, int addrSize, int cuEnd)
    {
        ulong offset = (ulong)cur.Pos;
        ulong code = cur.ULeb();
        if (code == 0) return null;                         // null DIE (sibling terminator)
        if (!abbrevs.TryGetValue(code, out var abbrev)) return null;

        var die = new Die { Tag = abbrev.Tag, Offset = offset };
        foreach (var (at, form, impl) in abbrev.Attrs)
        {
            object val = ReadForm(cur, form, addrSize, cuStart, impl);
            if (at != 0)
            {
                die.Attrs[at] = val;
                die.Forms[at] = form;
            }
        }
        _byOffset[offset] = die;

        if (abbrev.HasChildren)
        {
            while (cur.Pos < cuEnd)
            {
                var child = ReadDie(cur, abbrevs, cuStart, addrSize, cuEnd);
                if (child == null) break;
                die.Children.Add(child);
            }
        }
        return die;
    }

    private object ReadForm(ByteCursor c, int form, int addrSize, int cuStart, long impl)
    {
        switch (form)
        {
            case DW_FORM.addr: return c.UPtr(addrSize);
            case DW_FORM.data1: case DW_FORM.ref1: return form == DW_FORM.ref1 ? (ulong)cuStart + c.U8() : (ulong)c.U8();
            case DW_FORM.data2: case DW_FORM.ref2: return form == DW_FORM.ref2 ? (ulong)cuStart + c.U16() : (ulong)c.U16();
            case DW_FORM.data4: case DW_FORM.ref4: return form == DW_FORM.ref4 ? (ulong)cuStart + c.U32() : (ulong)c.U32();
            case DW_FORM.data8: case DW_FORM.ref8: return form == DW_FORM.ref8 ? (ulong)cuStart + c.U64() : c.U64();
            case DW_FORM.ref_udata: return (ulong)cuStart + c.ULeb();
            case DW_FORM.ref_addr: return (ulong)c.U32();
            case DW_FORM.sec_offset: return (ulong)c.U32();
            case DW_FORM.udata: return c.ULeb();
            case DW_FORM.sdata: return unchecked((ulong)c.SLeb());
            case DW_FORM.@string: return c.CStr();
            case DW_FORM.strp: return StrSection.At(_str, c.U32());
            case DW_FORM.line_strp: return StrSection.At(_lineStr, c.U32());
            case DW_FORM.flag: return (ulong)c.U8();
            case DW_FORM.flag_present: return (ulong)1;
            case DW_FORM.implicit_const: return unchecked((ulong)impl);
            case DW_FORM.exprloc: { int n = (int)c.ULeb(); return c.Bytes(n); }
            case DW_FORM.block1: { int n = c.U8(); return c.Bytes(n); }
            case DW_FORM.block2: { int n = c.U16(); return c.Bytes(n); }
            case DW_FORM.block4: { int n = (int)c.U32(); return c.Bytes(n); }
            case DW_FORM.block: { int n = (int)c.ULeb(); return c.Bytes(n); }
            case DW_FORM.data16: return c.Bytes(16);
            case DW_FORM.strx1: case DW_FORM.addrx1: c.U8(); return "";
            case DW_FORM.strx2: case DW_FORM.addrx2: c.U16(); return "";
            case DW_FORM.strx3: case DW_FORM.addrx3: c.Skip(3); return "";
            case DW_FORM.strx4: case DW_FORM.addrx4: c.U32(); return "";
            case DW_FORM.strx: case DW_FORM.addrx: c.ULeb(); return "";
            case DW_FORM.indirect: return ReadForm(c, (int)c.ULeb(), addrSize, cuStart, impl);
            default: throw new NotSupportedException($"DW_FORM 0x{form:X}");
        }
    }

    // ---- line-number program (DWARF v2-4) ---------------------------------

    private void ParseLineProgram(int offset, int addrSize, CompileUnit unit, List<string> filesOut)
    {
        if (offset >= _line.Length) return;
        var c = new ByteCursor(_line, _be, offset);
        uint unitLen = c.U32();
        int end = c.Pos + (int)unitLen;
        ushort ver = c.U16();
        if (ver >= 5) { c.U8(); c.U8(); }              // v5 address_size + segment_selector_size
        uint headerLen = c.U32();
        int programStart = c.Pos + (int)headerLen;
        int minInst = c.U8();
        int maxOps = ver >= 4 ? c.U8() : 1;
        bool defaultIsStmt = c.U8() != 0;
        sbyte lineBase = (sbyte)c.U8();
        int lineRange = c.U8();
        int opcodeBase = c.U8();
        var stdLens = new int[opcodeBase];
        for (int i = 1; i < opcodeBase; i++) stdLens[i] = c.U8();

        // v2-4 directory + file tables (v5 uses a different format handled below).
        var dirs = new List<string> { unit.CompDir };
        if (ver < 5)
        {
            while (true) { string d = c.CStr(); if (d.Length == 0) break; dirs.Add(d); }
            filesOut.Add("");                          // file index is 1-based
            while (true)
            {
                string name = c.CStr();
                if (name.Length == 0) break;
                int dirIdx = (int)c.ULeb(); c.ULeb(); c.ULeb();   // dir, mtime, size
                filesOut.Add(Combine(dirs, dirIdx, name));
            }
        }
        else
        {
            ParseV5FileTable(c, dirs, filesOut);
        }

        // Run the line-number state machine.
        c.Pos = programStart;
        ulong address = 0; int file = ver >= 5 ? 0 : 1, line = 1, column = 0; bool isStmt = defaultIsStmt, endSeq = false;
        void Emit()
        {
            unit.Lines.Add(new LineRow
            {
                Address = address,
                File = FileName(filesOut, file),
                Line = line,
                Column = column,
                IsStmt = isStmt,
                EndSequence = endSeq,
            });
        }
        void Reset() { address = 0; file = ver >= 5 ? 0 : 1; line = 1; column = 0; isStmt = defaultIsStmt; endSeq = false; }

        while (c.Pos < end)
        {
            int op = c.U8();
            if (op == 0)                                // extended opcode
            {
                int len = (int)c.ULeb();
                int opEnd = c.Pos + len;
                int sub = c.U8();
                switch (sub)
                {
                    case DW_LNE.end_sequence: endSeq = true; Emit(); Reset(); break;
                    case DW_LNE.set_address: address = c.UPtr(addrSize); break;
                    default: break;                     // define_file / set_discriminator / vendor
                }
                c.Pos = opEnd;
            }
            else if (op < opcodeBase)                   // standard opcode
            {
                switch (op)
                {
                    case DW_LNS.copy: Emit(); break;
                    case DW_LNS.advance_pc: address += (ulong)c.ULeb() * (ulong)minInst; break;
                    case DW_LNS.advance_line: line += (int)c.SLeb(); break;
                    case DW_LNS.set_file: file = (int)c.ULeb(); break;
                    case DW_LNS.set_column: column = (int)c.ULeb(); break;
                    case DW_LNS.negate_stmt: isStmt = !isStmt; break;
                    case DW_LNS.set_basic_block: break;
                    case DW_LNS.const_add_pc: address += (ulong)((255 - opcodeBase) / lineRange) * (ulong)minInst; break;
                    case DW_LNS.fixed_advance_pc: address += c.U16(); break;
                    case DW_LNS.set_prologue_end: case DW_LNS.set_epilogue_begin: break;
                    case DW_LNS.set_isa: c.ULeb(); break;
                    default: for (int i = 0; i < stdLens[op]; i++) c.ULeb(); break;  // unknown: skip operands
                }
            }
            else                                        // special opcode
            {
                int adj = op - opcodeBase;
                address += (ulong)(adj / lineRange) * (ulong)minInst;
                line += lineBase + (adj % lineRange);
                Emit();
            }
        }
    }

    // DWARF v5 directory_entry_format / file_name_entry_format tables. clang -g
    // defaults to v5, so unlike the 360 reader (v4 only) the OG one must parse it.
    private void ParseV5FileTable(ByteCursor c, List<string> dirs, List<string> filesOut)
    {
        // directories
        int dirFmtCount = c.U8();
        var dirForms = new List<(int content, int form)>();
        for (int i = 0; i < dirFmtCount; i++) dirForms.Add(((int)c.ULeb(), (int)c.ULeb()));
        int dirCount = (int)c.ULeb();
        dirs.Clear();
        for (int i = 0; i < dirCount; i++)
        {
            string path = "";
            foreach (var (content, form) in dirForms)
            {
                object v = ReadLineFormValue(c, form);
                if (content == 1 /*DW_LNCT_path*/ && v is string s) path = s;
            }
            dirs.Add(path);
        }
        // files
        int fileFmtCount = c.U8();
        var fileForms = new List<(int content, int form)>();
        for (int i = 0; i < fileFmtCount; i++) fileForms.Add(((int)c.ULeb(), (int)c.ULeb()));
        int fileCount = (int)c.ULeb();
        for (int i = 0; i < fileCount; i++)
        {
            string name = ""; int dirIdx = 0;
            foreach (var (content, form) in fileForms)
            {
                object v = ReadLineFormValue(c, form);
                if (content == 1 /*path*/ && v is string s) name = s;
                else if (content == 2 /*directory_index*/ && v is ulong u) dirIdx = (int)u;
            }
            filesOut.Add(Combine(dirs, dirIdx, name));
        }
    }

    // A subset of forms usable in the v5 line-table format tables.
    private object ReadLineFormValue(ByteCursor c, int form) => form switch
    {
        DW_FORM.@string => c.CStr(),
        DW_FORM.line_strp => StrSection.At(_lineStr, c.U32()),
        DW_FORM.strp => StrSection.At(_str, c.U32()),
        DW_FORM.data1 => (ulong)c.U8(),
        DW_FORM.data2 => (ulong)c.U16(),
        DW_FORM.data4 => (ulong)c.U32(),
        DW_FORM.data8 => c.U64(),
        DW_FORM.data16 => (object)c.Bytes(16),
        DW_FORM.udata => c.ULeb(),
        DW_FORM.block => c.Bytes((int)c.ULeb()),
        _ => throw new NotSupportedException($"line DW_FORM 0x{form:X}"),
    };

    private static string Combine(List<string> dirs, int dirIdx, string name)
    {
        if (name.Length > 1 && (name[1] == ':' || name[0] == '/' || name[0] == '\\')) return name; // absolute
        string dir = dirIdx >= 0 && dirIdx < dirs.Count ? dirs[dirIdx] : "";
        return dir.Length == 0 ? name : dir.TrimEnd('/', '\\') + "\\" + name;
    }

    private static string FileName(List<string> files, int index) =>
        index >= 0 && index < files.Count ? files[index] : "";
}
