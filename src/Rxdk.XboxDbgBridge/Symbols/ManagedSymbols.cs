using Rxdk.Pdb;
using Rxdk.Pdb.Symbols;
using Rxdk.Pdb.Tpi;
using Rxdk.Xbdm;

namespace Rxdk.XboxDbgBridge.Symbols;

/// <summary>
/// Locals emission backed by the pure-managed <see cref="PdbImage"/> reader instead of dbghelp.
/// dbghelp cannot interpret the modern S_LOCAL / S_DEFRANGE_FRAMEPOINTER_REL records that Zig/LLVM
/// emit (it reports a def-range's code span as the variable size, so a 4-byte HRESULT shows as
/// array[64]); the managed reader resolves the real type/size/frame-offset. Falls back to dbghelp
/// via <see cref="SymbolService"/> only when this path yields nothing.
/// </summary>
internal sealed class ManagedSymbols
{
    private readonly PdbImage _pdb;
    private readonly nuint _moduleBase;

    internal ManagedSymbols(PdbImage pdb, nuint moduleBase)
    {
        _pdb = pdb;
        _moduleBase = moduleBase;
    }

    /// <summary>Emits the current frame's locals; returns false if no frame was found at EIP.</summary>
    internal bool EmitLocals(ref XbdmContext context, VariableJson variables, KitMemoryAccess memory)
    {
        var frame = FindFrame(context.Eip);
        if (frame is null)
            return false;

        var emitted = false;
        foreach (var local in frame.Locals)
        {
            if (variables.IsFull)
                break;
            if (IsHidden(local.Name) || variables.WasEmitted(local.Name))
                continue;

            var address = ResolveLocalAddress(frame, local, ref context);
            EmitValue(local.Name, local.TypeIndex, address, memory, variables, expandBase: local.Name);
            emitted = true;
        }

        return emitted;
    }

    /// <summary>
    /// Emits the program's global-scope data symbols (globals + file-statics), formatting each at its
    /// absolute kit address (moduleBase + RVA). Compiler-generated helpers and publics are skipped.
    /// <paramref name="maxTier"/> caps how far past the title's own mutable globals to include
    /// (0 = title .data only, 1 = + title const tables, 2 = + linked-library globals); it is driven
    /// by the extension's "Globals visibility" toggle. Returns false when the PDB has no usable data
    /// globals (so the map fallback can take over).
    /// </summary>
    internal bool EmitGlobals(VariableJson variables, KitMemoryAccess memory, int maxVars, int maxTier)
    {
        if (_moduleBase == 0)
            return false;

        // Rank the program's globals so the Globals pane shows the most useful set. A title links
        // hundreds of library globals (D3D::*, CRT stdio, ...) plus compile-time const lookup tables
        // from system headers (e.g. D3DTEXTUREDIRECTENCODE, emitted into the title's own .obj but
        // living in read-only .rdata). What the user actually debugs is mutable program state:
        //   tier 0 = title compiland + writable section (.data/.bss) — the real globals
        //   tier 1 = title compiland, any section                    — include title's const tables
        //   tier 2 = everything                                      — last-resort, library globals
        // The toggle sets maxTier; we show every tier up to it, but relax upward if that would leave
        // the pane empty (e.g. a title with no writable globals) so there's always something to see.
        var candidates = new List<(GlobalSymbol Global, nuint Address, int Tier)>();
        foreach (var global in _pdb.EnumerateGlobals())
        {
            if (global.IsPublic || IsHiddenGlobal(global.Name))
                continue;
            var rva = _pdb.Dbi.SectionOffsetToRva(global.Section, (int)global.Offset);
            if (rva == 0)
                continue;
            var address = _moduleBase + (nuint)rva;
            var module = _pdb.Dbi.FindModuleByRva(rva);
            var isTitle = module is not null && !IsLibraryObject(module.ObjectFileName);
            var tier = isTitle ? (IsWritableSection(global.Section) ? 0 : 1) : 2;
            candidates.Add((global, address, tier));
        }

        var showTier = Math.Clamp(maxTier, 0, 2);
        while (showTier < 2 && !candidates.Exists(c => c.Tier <= showTier))
            showTier++;

        var emitted = false;
        foreach (var (global, address, tier) in candidates)
        {
            if (variables.IsFull || variables.Count >= maxVars)
                break;
            if (tier > showTier)
                continue;

            var display = CleanGlobalName(global.Name);
            if (variables.WasEmitted(display))
                continue;

            // Display the readable name but expand under the raw symbol name, since TryEmitMembers
            // matches globals by their raw stream name.
            EmitValue(display, global.TypeIndex, address, memory, variables, expandBase: global.Name);
            emitted = true;
        }

        return emitted;
    }

    /// <summary>Expands one aggregate local (array elements or struct members) by name.</summary>
    internal bool TryEmitMembers(string expression, ref XbdmContext context, VariableJson variables, KitMemoryAccess memory)
    {
        // Synthetic children (container elements, aggregate members) carry an address+type reference
        // "@<hexaddr>#<typeindex>" as their expand key -- self-contained, so heap nodes scattered by
        // a list/tree re-expand without a field-path expression that can't name them.
        if (TryParseAddrRef(expression, out var refAddr, out var refType))
        {
            EmitChildren(refType, refAddr, memory, variables);
            return variables.Count > 0;
        }

        // Otherwise the key is a source expression (a top-level variable, e.g. "g_AntiAliasModes"):
        // resolve the base then walk any member/index/deref accessors to the target address+type.
        if (!ExpressionPath.TryParse(expression, out var baseName, out var accessors))
            return false;
        if (!TryResolveBase(baseName, ref context, memory, out var address, out var typeIndex))
            return false;

        if (accessors.Count > 0)
        {
            var evaluator = new TypeEvaluator(_pdb.Types, a => memory.ReadDword((nuint)a));
            if (!evaluator.TryWalk(address, typeIndex, accessors, out var finalAddress, out var finalType, out _))
                return false;
            address = (nuint)finalAddress;
            typeIndex = finalType.TypeIndex;
        }

        EmitChildren(typeIndex, address, memory, variables);
        return variables.Count > 0;
    }

    private static string AddrRef(nuint address, uint typeIndex) => $"@{(ulong)address:x}#{typeIndex}";

    private static bool TryParseAddrRef(string s, out nuint address, out uint typeIndex)
    {
        address = 0;
        typeIndex = 0;
        if (s.Length < 2 || s[0] != '@')
            return false;
        var hash = s.IndexOf('#');
        if (hash < 2)
            return false;
        try
        {
            address = (nuint)Convert.ToUInt64(s.Substring(1, hash - 1), 16);
            typeIndex = uint.Parse(s.Substring(hash + 1));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGlobalAddress(GlobalSymbol global, out nuint address)
    {
        address = 0;
        var rva = _pdb.Dbi.SectionOffsetToRva(global.Section, (int)global.Offset);
        if (rva == 0)
            return false;
        address = _moduleBase + (nuint)rva;
        return true;
    }

    // A module contributed by a static library records the .lib as its object file; the title's own
    // compiland records its .obj/.o. That distinguishes program globals from linked-in library ones.
    private static bool IsLibraryObject(string objectFile) =>
        objectFile.EndsWith(".lib", StringComparison.OrdinalIgnoreCase);

    // IMAGE_SCN_MEM_WRITE — set on .data/.bss (mutable program state), clear on .rdata (compile-time
    // constants). Mutable globals are what a debugger's Globals pane is for; const tables are noise.
    private const uint ImageScnMemWrite = 0x80000000;

    private bool IsWritableSection(ushort section)
    {
        var sections = _pdb.Dbi.Sections;
        if (section == 0 || section > sections.Count)
            return false;
        return (sections[section - 1].Characteristics & ImageScnMemWrite) != 0;
    }

    // File-static / anonymous-namespace globals carry a `anonymous namespace':: qualifier in their
    // symbol name; strip it for display (kept only as a scope marker, not part of the variable name).
    private static string CleanGlobalName(string name)
    {
        const string anon = "`anonymous namespace'::";
        return name.StartsWith(anon, StringComparison.Ordinal) ? name[anon.Length..] : name;
    }

    private FrameInfo? FindFrame(uint eip)
    {
        if (_moduleBase == 0 || eip < _moduleBase)
            return null;
        return _pdb.FindFrame(eip - (uint)_moduleBase); // RVA = EIP - module base (image base == PDB base)
    }

    private void EmitValue(string name, uint typeIndex, nuint address, KitMemoryAccess memory, VariableJson variables, string expandBase)
    {
        var type = _pdb.Types.Resolve(typeIndex);
        var label = Describe(type, address, memory, out var expandable);
        variables.Append(name, label, expandable, expandBase);
    }

    /// <summary>
    /// Renders a resolved value for a single row: an aggregate/array shows a type summary and is
    /// marked expandable; a scalar/pointer is formatted from its bytes. Modifiers (const/volatile) are
    /// peeled first so a const struct or pointer is recognized rather than shown as a raw dword.
    /// </summary>
    private string Describe(PdbType type, nuint address, KitMemoryAccess memory, out bool expandable)
    {
        type = _pdb.Types.Peel(type.TypeIndex);
        switch (type.Kind)
        {
            case PdbTypeKind.Array:
            {
                var elem = type.ReferentType != 0 ? _pdb.Types.Resolve(type.ReferentType) : null;
                expandable = type.ElementCount > 0;
                return elem?.Name is { Length: > 0 } en ? $"{en}[{type.ElementCount}]" : $"array[{type.ElementCount}]";
            }

            case PdbTypeKind.Struct:
            case PdbTypeKind.Class:
            case PdbTypeKind.Union:
                if (TryFormatString(type, address, memory, out var sval))
                {
                    expandable = false;
                    return sval;
                }
                // LARGE_INTEGER / ULARGE_INTEGER are 64-bit-integer unions (QuadPart overlaps
                // LowPart/HighPart); a timer-heavy title has these everywhere. The bare type name is
                // useless, so show QuadPart inline like a real debugger while still letting the row
                // expand to its members.
                if (TryFormatLargeInteger(type, address, memory, out var liVal))
                {
                    expandable = type.Members.Count > 0;
                    return liVal;
                }
                if (TryContainerSummary(type, address, memory, out var csummary, out expandable))
                    return csummary;
                expandable = type.Members.Count > 0;
                return type.Name is { Length: > 0 } tn ? tn : $"{{{type.ByteSize} bytes}}";

            case PdbTypeKind.Enum:
                expandable = false;
                return FormatEnum(type, address, memory);

            case PdbTypeKind.Pointer:
            {
                // A pointer to an aggregate/array is expandable: expanding dereferences it to show the
                // pointee's members (so `this` and any struct/class* -- e.g. m_pd3dDevice -- drill in).
                // char*/wchar_t* are rendered as strings (referent is a primitive => not expandable),
                // and void*/pointer-to-pointer stay non-expandable.
                var referent = type.ReferentType != 0 ? _pdb.Types.Peel(type.ReferentType) : null;
                expandable = referent is not null &&
                    ((referent.IsAggregate && referent.Members.Count > 0) ||
                     (referent.Kind == PdbTypeKind.Array && referent.ElementCount > 0));
                return FormatPointer(type, address, memory);
            }

            default:
                expandable = false;
                return FormatScalar(type, address, memory);
        }
    }

    /// <summary>
    /// Evaluates a watch/hover expression: a bare symbol, a register, or a member/index/deref chain
    /// (<c>a.b</c>, <c>p-&gt;field</c>, <c>arr[2]</c>) over the current frame's locals or the program's
    /// globals. This is the managed replacement for dbghelp's expression engine and works on every
    /// platform. Returns false with a short <paramref name="error"/> token on any failure.
    /// </summary>
    internal bool TryEvaluate(string expression, ref XbdmContext context, KitMemoryAccess memory, out string value, out string? error, out bool expandable)
    {
        value = string.Empty;
        error = null;
        expandable = false;

        if (!ExpressionPath.TryParse(expression, out var baseName, out var accessors))
        {
            error = "badExpression";
            return false;
        }

        // A bare register name (EAX, ESP, ...) with no member/index chain reads straight from context.
        if (accessors.Count == 0 && TryReadRegister(baseName, ref context, out var register))
        {
            value = $"0x{register:x8}";
            return true;
        }

        if (!TryResolveBase(baseName, ref context, memory, out var address, out var typeIndex))
        {
            error = "symbolNotFound";
            return false;
        }

        var evaluator = new TypeEvaluator(_pdb.Types, a => memory.ReadDword((nuint)a));
        if (!evaluator.TryWalk(address, typeIndex, accessors, out var finalAddress, out var finalType, out var walkError))
        {
            error = walkError ?? "evalFailed";
            return false;
        }

        value = Describe(finalType, (nuint)finalAddress, memory, out expandable);
        return true;
    }

    /// <summary>
    /// Computes a local's live address from the frame register its offset is measured against.
    /// EBP-relative is the common -O0 case; VFRAME is the realigned virtual frame base a function
    /// with 16-byte-aligned locals uses (VFRAME = (EBP - callee-saved bytes) &amp; ~0xF, matching the
    /// prologue's `and esp,-0x10` after pushing the saved regs); ESP-relative appears in
    /// frame-pointer-omitted functions.
    /// </summary>
    private static nuint ResolveLocalAddress(FrameInfo frame, LocalVariable local, ref XbdmContext context)
    {
        long baseValue = local.Base switch
        {
            FrameBase.Esp => context.Esp,
            FrameBase.VFrame => ((long)context.Ebp - frame.CalleeSavedBytes) & ~0xFL,
            _ => context.Ebp,
        };
        return (nuint)(baseValue + local.FrameOffset);
    }

    /// <summary>
    /// Resolves a bare name as a member of the current method's <c>this</c> object (including
    /// inherited members). Reads the live <c>this</c> pointer from its frame slot, then walks the
    /// pointed-to class for <paramref name="name"/>. Returns the member's absolute address + type.
    /// </summary>
    private bool TryResolveThisMember(
        string name, FrameInfo frame, ref XbdmContext context, KitMemoryAccess memory,
        out nuint address, out uint typeIndex)
    {
        address = 0;
        typeIndex = 0;

        var thisLocal = frame.Locals.FirstOrDefault(l => string.Equals(l.Name, "this", StringComparison.Ordinal));
        if (thisLocal is null)
            return false;

        var thisPtr = memory.ReadDword(ResolveLocalAddress(frame, thisLocal, ref context));
        if (thisPtr is null or 0)
            return false;

        var pointerType = _pdb.Types.Peel(thisLocal.TypeIndex);
        if (pointerType.Kind != PdbTypeKind.Pointer || pointerType.ReferentType == 0)
            return false;

        if (!_pdb.Types.TryFindMember(pointerType.ReferentType, name, out var offset, out var memberType))
            return false;

        address = (nuint)(thisPtr.Value + offset);
        typeIndex = memberType;
        return true;
    }

    /// <summary>
    /// Resolves a base symbol name to its address and type: a frame local first, then an implicit
    /// member of the enclosing object (<c>this-&gt;name</c>, including inherited members), then a
    /// global. The implicit-<c>this</c> step is what lets bare member names -- the form the Autos
    /// window and natural watch expressions use -- resolve inside a method.
    /// </summary>
    private bool TryResolveBase(string name, ref XbdmContext context, KitMemoryAccess memory, out nuint address, out uint typeIndex)
    {
        address = 0;
        typeIndex = 0;

        var frame = FindFrame(context.Eip);
        if (frame is not null)
        {
            var local = frame.Locals.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal))
                        ?? frame.Locals.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (local is not null)
            {
                address = ResolveLocalAddress(frame, local, ref context);
                typeIndex = local.TypeIndex;
                return true;
            }

            if (TryResolveThisMember(name, frame, ref context, memory, out address, out typeIndex))
                return true;
        }

        foreach (var global in _pdb.EnumerateGlobals())
        {
            if (global.IsPublic)
                continue;
            if (!string.Equals(global.Name, name, StringComparison.Ordinal) &&
                !string.Equals(CleanGlobalName(global.Name), name, StringComparison.Ordinal))
                continue;
            if (!TryGlobalAddress(global, out address))
                continue;
            typeIndex = global.TypeIndex;
            return true;
        }

        return false;
    }

    private static bool TryReadRegister(string name, ref XbdmContext context, out uint value)
    {
        switch (name.ToUpperInvariant())
        {
            case "EAX": value = context.Eax; return true;
            case "EBX": value = context.Ebx; return true;
            case "ECX": value = context.Ecx; return true;
            case "EDX": value = context.Edx; return true;
            case "ESI": value = context.Esi; return true;
            case "EDI": value = context.Edi; return true;
            case "EBP": value = context.Ebp; return true;
            case "ESP": value = context.Esp; return true;
            case "EIP": value = context.Eip; return true;
            case "EFLAGS": value = context.EFlags; return true;
            default: value = 0; return false;
        }
    }

    // A type whose (peeled) name is the given std container, e.g. IsStdContainer(name, "vector") for
    // "std::__N::vector<int, ...>". The "::" guard avoids matching a user type like my_vector<>.
    private static bool IsStdContainer(string? name, string container) =>
        name is not null && (name.Contains($"::{container}<", StringComparison.Ordinal) ||
                             name.StartsWith($"{container}<", StringComparison.Ordinal));

    // libc++ std::vector is [__begin_, __end_) of contiguous T. Reads those two pointers and the
    // element type/size so both the summary (size) and the element rows can be produced. Returns
    // false for anything that isn't a vector-shaped type, so callers fall back to raw members.
    private bool TryVectorInfo(PdbType type, nuint address, KitMemoryAccess memory,
        out uint begin, out uint count, out uint elemTypeIndex, out uint elemSize)
    {
        begin = count = elemTypeIndex = elemSize = 0;
        type = _pdb.Types.Peel(type.TypeIndex);
        if (!IsStdContainer(type.Name, "vector"))
            return false;
        if (!_pdb.Types.TryFindMember(type.TypeIndex, "__begin_", out var beginOff, out var beginTypeIdx) ||
            !_pdb.Types.TryFindMember(type.TypeIndex, "__end_", out var endOff, out _))
            return false;

        var b = memory.ReadDword(address + (nuint)beginOff);
        var e = memory.ReadDword(address + (nuint)endOff);
        if (b is null || e is null)
            return false;

        var ptr = _pdb.Types.Peel(beginTypeIdx);
        if (ptr.Kind != PdbTypeKind.Pointer)
            return false;
        // Element type: an LF_POINTER records its referent; a primitive pointer (e.g. int* as
        // T_32PINT4) carries the base type in the low byte of its index (the pointer-mode bits clear).
        elemTypeIndex = ptr.ReferentType != 0 ? ptr.ReferentType : (ptr.TypeIndex & 0xFF);
        if (elemTypeIndex == 0)
            return false;

        elemSize = _pdb.Types.Resolve(elemTypeIndex).ByteSize is var s && s != 0 ? s : 1u;
        begin = b.Value;
        count = e.Value >= b.Value ? (e.Value - b.Value) / elemSize : 0;
        return true;
    }

    // std::string (libc++ SSO). The first byte's low bit is __is_long_: short strings keep the text
    // inline at offset 1; long strings hold a char* at offset 8. Renders the text quoted. (char only;
    // wstring falls through to the raw view.)
    private bool TryFormatString(PdbType type, nuint address, KitMemoryAccess memory, out string value)
    {
        value = string.Empty;
        if (type.Name is null || !type.Name.Contains("basic_string<char,", StringComparison.Ordinal))
            return false;
        var b0 = memory.ReadDword(address);
        if (b0 is null)
            return false;
        uint dataAddr;
        if ((b0.Value & 1) != 0) // long: char* at __long.__data_ (offset 8)
        {
            var d = memory.ReadDword(address + 8);
            if (d is null)
                return false;
            dataAddr = d.Value;
        }
        else // short: inline buffer at __short.__data_ (offset 1)
        {
            dataAddr = (uint)(address + 1);
        }
        value = ReadCString(dataAddr, 1, memory);
        return true;
    }

    // A "{ size=N }" summary (and expandability) for a recognized container, so the row shows a count
    // instead of the giant template type name. False for anything not a known container.
    /// <summary>
    /// Formats a LARGE_INTEGER / ULARGE_INTEGER union as its 64-bit QuadPart (signed for
    /// LARGE_INTEGER, unsigned for ULARGE_INTEGER), e.g. <c>133776… (0x01db…)</c>. Returns false for
    /// any other union so the generic path handles it.
    /// </summary>
    private bool TryFormatLargeInteger(PdbType type, nuint address, KitMemoryAccess memory, out string value)
    {
        value = string.Empty;
        var unsigned = string.Equals(type.Name, "_ULARGE_INTEGER", StringComparison.Ordinal);
        if (!unsigned && !string.Equals(type.Name, "_LARGE_INTEGER", StringComparison.Ordinal))
            return false;
        var lo = memory.ReadDword(address);
        var hi = memory.ReadDword(address + 4);
        if (lo is null || hi is null)
            return false;
        var q = ((ulong)hi.Value << 32) | lo.Value;
        value = unsigned ? $"{q} (0x{q:x})" : $"{(long)q} (0x{q:x})";
        return true;
    }

    private bool TryContainerSummary(PdbType type, nuint address, KitMemoryAccess memory, out string summary, out bool expandable)
    {
        summary = string.Empty;
        expandable = false;
        type = _pdb.Types.Peel(type.TypeIndex);
        uint count;
        if (TryVectorInfo(type, address, memory, out _, out count, out _, out _))
        {
            // count already set by TryVectorInfo
        }
        else if (IsStdContainer(type.Name, "array") &&
                 _pdb.Types.TryFindMember(type.TypeIndex, "__elems_", out _, out var arrTypeIdx))
        {
            count = _pdb.Types.Resolve(arrTypeIdx).ElementCount;
        }
        else if (IsStdContainer(type.Name, "list") &&
                 _pdb.Types.TryFindMember(type.TypeIndex, "__size_", out var listSizeOff, out _))
        {
            count = memory.ReadDword(address + (nuint)listSizeOff) ?? 0;
        }
        else if ((IsStdContainer(type.Name, "set") || IsStdContainer(type.Name, "map")) &&
                 _pdb.Types.TryFindMember(type.TypeIndex, "__tree_", out var treeOff, out var treeType) &&
                 _pdb.Types.TryFindMember(treeType, "__size_", out var treeSizeOff, out _))
        {
            count = memory.ReadDword((nuint)(address + (nuint)treeOff + (nuint)treeSizeOff)) ?? 0;
        }
        else
        {
            return false;
        }

        summary = $"{{ size={count} }}";
        expandable = count > 0;
        return true;
    }

    // Emits one indexed element ("[i]") with a self-contained address+type expand key.
    private void EmitElement(uint index, uint typeIndex, nuint address, KitMemoryAccess memory, VariableJson variables) =>
        EmitValue($"[{index}]", typeIndex, address, memory, variables, expandBase: AddrRef(address, typeIndex));

    // Emits a vector's elements as [i] rows. Returns true if the type was a vector (so raw members
    // are suppressed), even when empty.
    private bool TryEmitVector(PdbType type, nuint address, KitMemoryAccess memory, VariableJson variables)
    {
        if (!TryVectorInfo(type, address, memory, out var begin, out var count, out var elemTypeIndex, out var elemSize))
            return false;
        var shown = Math.Min(count, 1024u);
        for (uint i = 0; i < shown && !variables.IsFull; i++)
            EmitElement(i, elemTypeIndex, (nuint)(begin + i * elemSize), memory, variables);
        return true;
    }

    // std::array<T,N> wraps a single T[N] member (__elems_). Show its elements directly as [i]
    // rather than nesting them under __elems_.
    private bool TryEmitArray(PdbType type, nuint address, KitMemoryAccess memory, VariableJson variables)
    {
        if (!IsStdContainer(type.Name, "array") ||
            !_pdb.Types.TryFindMember(type.TypeIndex, "__elems_", out var elemsOff, out var elemsTypeIdx))
            return false;
        var arr = _pdb.Types.Resolve(elemsTypeIdx);
        if (arr.Kind != PdbTypeKind.Array || arr.ElementCount == 0)
            return false;
        var elemSize = _pdb.Types.Resolve(arr.ReferentType).ByteSize is var s && s != 0 ? s : 1u;
        var baseAddr = address + (nuint)elemsOff;
        var count = Math.Min(arr.ElementCount, 1024u);
        for (uint i = 0; i < count && !variables.IsFull; i++)
            EmitElement(i, arr.ReferentType, baseAddr + (nuint)(i * elemSize), memory, variables);
        return true;
    }

    // Resolves the value type a node-based container stores: via its node allocator's type name
    // (allocator<NODE>), finds NODE, then NODE's __value_ member (its offset within a node + type).
    private bool TryNodeValue(uint containerTypeIndex, out uint nodeTypeIndex, out uint valueOffset, out uint valueTypeIndex)
    {
        nodeTypeIndex = 0; valueOffset = 0; valueTypeIndex = 0;
        uint allocType;
        if (!_pdb.Types.TryFindMember(containerTypeIndex, "__node_alloc_", out _, out allocType) &&
            !(_pdb.Types.TryFindMember(containerTypeIndex, "__tree_", out _, out var treeType) &&
              _pdb.Types.TryFindMember(treeType, "__node_alloc_", out _, out allocType)))
            return false;
        var nodeName = ExtractAllocatorArg(_pdb.Types.Resolve(allocType).Name);
        if (nodeName is null || !_pdb.Types.TryFindByName(nodeName, out var nodeType))
            return false;
        nodeTypeIndex = nodeType.TypeIndex;
        return _pdb.Types.TryFindMember(nodeType.TypeIndex, "__value_", out valueOffset, out valueTypeIndex);
    }

    // "std::..::allocator<NODE >" -> "NODE" (angle-bracket balanced, trailing space trimmed).
    private static string? ExtractAllocatorArg(string? name)
    {
        if (name is null) return null;
        var i = name.IndexOf("allocator<", StringComparison.Ordinal);
        if (i < 0) return null;
        i += "allocator<".Length;
        int depth = 1, start = i;
        for (; i < name.Length && depth > 0; i++)
        {
            if (name[i] == '<') depth++;
            else if (name[i] == '>') depth--;
        }
        return depth == 0 ? name.Substring(start, i - 1 - start).Trim() : null;
    }

    // std::list: a circular doubly-linked list with an embedded sentinel node __end_ whose __next_
    // is the first element. Walks __next_ until back at the sentinel.
    private bool TryEmitList(PdbType type, nuint address, KitMemoryAccess memory, VariableJson variables)
    {
        if (!IsStdContainer(type.Name, "list"))
            return false;
        if (!TryNodeValue(type.TypeIndex, out var nodeTypeIdx, out var valueOff, out var valueTypeIdx) ||
            !_pdb.Types.TryFindMember(type.TypeIndex, "__end_", out var endOff, out _) ||
            !_pdb.Types.TryFindMember(type.TypeIndex, "__size_", out var sizeOff, out _) ||
            !_pdb.Types.TryFindMember(nodeTypeIdx, "__next_", out var nextOff, out _))
            return false;

        var size = memory.ReadDword(address + (nuint)sizeOff) ?? 0;
        var sentinel = (uint)(address + (nuint)endOff);
        var node = memory.ReadDword((nuint)(sentinel + (uint)nextOff));
        for (uint i = 0; node is not null && node.Value != 0 && node.Value != sentinel && i < size && i < 4096 && !variables.IsFull; i++)
        {
            EmitElement(i, valueTypeIdx, (nuint)(node.Value + (uint)valueOff), memory, variables);
            node = memory.ReadDword((nuint)(node.Value + (uint)nextOff));
        }
        return true;
    }

    // std::set / std::map: a red-black tree. In-order walk from __begin_node_ (leftmost) to the
    // __end_node_ sentinel via left/right/parent links (libc++ __tree_next).
    private bool TryEmitTree(PdbType type, nuint address, KitMemoryAccess memory, VariableJson variables)
    {
        if (!IsStdContainer(type.Name, "set") && !IsStdContainer(type.Name, "map"))
            return false;
        if (!_pdb.Types.TryFindMember(type.TypeIndex, "__tree_", out var treeOff, out var treeType) ||
            !TryNodeValue(type.TypeIndex, out var nodeTypeIdx, out var valueOff, out var valueTypeIdx) ||
            !_pdb.Types.TryFindMember(treeType, "__begin_node_", out var beginOff, out _) ||
            !_pdb.Types.TryFindMember(treeType, "__end_node_", out var endNodeOff, out _) ||
            !_pdb.Types.TryFindMember(treeType, "__size_", out var sizeOff, out _) ||
            !_pdb.Types.TryFindMember(nodeTypeIdx, "__left_", out var leftOff, out _) ||
            !_pdb.Types.TryFindMember(nodeTypeIdx, "__right_", out var rightOff, out _) ||
            !_pdb.Types.TryFindMember(nodeTypeIdx, "__parent_", out var parentOff, out _))
            return false;

        var treeAddr = (uint)(address + (nuint)treeOff);
        var endNode = treeAddr + (uint)endNodeOff; // &__end_node_ (the past-the-end sentinel)
        var size = memory.ReadDword((nuint)(treeAddr + (uint)sizeOff)) ?? 0;
        var node = memory.ReadDword((nuint)(treeAddr + (uint)beginOff)); // __begin_node_ (leftmost)
        for (uint i = 0; node is not null && node.Value != 0 && node.Value != endNode && i < size && i < 4096 && !variables.IsFull; i++)
        {
            EmitElement(i, valueTypeIdx, (nuint)(node.Value + (uint)valueOff), memory, variables);
            node = TreeNext(node.Value, endNode, (uint)leftOff, (uint)rightOff, (uint)parentOff, memory);
        }
        return true;
    }

    // The in-order successor of a red-black tree node (bounded to avoid a runaway on a corrupt tree).
    private static uint? TreeNext(uint node, uint endNode, uint leftOff, uint rightOff, uint parentOff, KitMemoryAccess memory)
    {
        var right = memory.ReadDword((nuint)(node + rightOff)) ?? 0;
        if (right != 0)
        {
            var n = right; // leftmost of the right subtree
            for (var g = 0; g < 4096; g++)
            {
                var l = memory.ReadDword((nuint)(n + leftOff)) ?? 0;
                if (l == 0) break;
                n = l;
            }
            return n;
        }
        for (var g = 0; g < 4096; g++) // walk up until node is its parent's left child
        {
            var parent = memory.ReadDword((nuint)(node + parentOff)) ?? 0;
            if (parent == 0 || parent == endNode)
                return endNode;
            if ((memory.ReadDword((nuint)(parent + leftOff)) ?? 0) == node)
                return parent;
            node = parent;
        }
        return endNode;
    }

    private void EmitChildren(uint typeIndex, nuint address, KitMemoryAccess memory, VariableJson variables)
    {
        var type = _pdb.Types.Resolve(typeIndex);

        // A pointer expands by dereferencing: read the pointee address at `address`, then show the
        // referent's children. This is what makes `this` (a CXBoxSample*) and every struct/class*
        // member browsable. Peel modifiers first (a const pointer is still a pointer); a null or
        // unreadable pointer simply yields no rows.
        var peeled = _pdb.Types.Peel(type.TypeIndex);
        if (peeled.Kind == PdbTypeKind.Pointer && peeled.ReferentType != 0)
        {
            var target = memory.ReadDword(address);
            if (target is null || target.Value == 0)
                return;
            EmitChildren(peeled.ReferentType, (nuint)target.Value, memory, variables);
            return;
        }

        // Recognized standard containers show their elements instead of internal pointers/nodes.
        if (TryEmitVector(type, address, memory, variables) ||
            TryEmitArray(type, address, memory, variables) ||
            TryEmitList(type, address, memory, variables) ||
            TryEmitTree(type, address, memory, variables))
            return;

        if (type.Kind == PdbTypeKind.Array && type.ElementCount > 0)
        {
            var elem = _pdb.Types.Resolve(type.ReferentType);
            var elemSize = elem.ByteSize == 0 ? 4u : elem.ByteSize;
            var count = Math.Min(type.ElementCount, 256u);
            for (var i = 0u; i < count && !variables.IsFull; i++)
                EmitElement(i, type.ReferentType, address + (nuint)(i * elemSize), memory, variables);
            return;
        }

        if (type.IsAggregate)
        {
            foreach (var member in type.Members)
            {
                if (variables.IsFull)
                    break;
                // A nameless member is a base class (recorded so member lookup recurses into it);
                // flatten its members into the derived view instead of showing a blank row.
                if (string.IsNullOrEmpty(member.Name))
                {
                    EmitChildren(member.TypeIndex, address + (nuint)member.Offset, memory, variables);
                    continue;
                }
                var memberAddr = address + (nuint)member.Offset;
                EmitValue(member.Name, member.TypeIndex, memberAddr, memory, variables,
                    expandBase: AddrRef(memberAddr, member.TypeIndex));
            }
        }
    }

    // An enum value shown by name (falling back to the number when no enumerator matches).
    private string FormatEnum(PdbType type, nuint address, KitMemoryAccess memory)
    {
        var raw = memory.ReadDword(address);
        if (raw is null)
            return "<unreadable>";
        var v = type.ByteSize switch { 1 => raw.Value & 0xFF, 2 => raw.Value & 0xFFFF, _ => raw.Value };
        foreach (var e in type.Members)
            if (unchecked((uint)e.Offset) == v)
                return $"{e.Name} ({(int)v})";
        return $"{(int)v} (0x{v:x})";
    }

    // A pointer: the address, plus the pointed-to text when it's a char*/wchar_t* string.
    private string FormatPointer(PdbType type, nuint address, KitMemoryAccess memory)
    {
        var ptr = memory.ReadDword(address);
        if (ptr is null)
            return "<unreadable>";
        var value = ptr.Value;
        var width = PointerCharWidth(type);
        if (value != 0 && width != 0)
            return $"0x{value:x8} {ReadCString(value, width, memory)}";
        return $"0x{value:x8}";
    }

    // 1 for char*/signed/unsigned char*, 2 for wchar_t*, 0 for any other pointer. Handles both the
    // LF_POINTER form (referent type set) and the primitive-pointer form (base type in the low byte).
    private int PointerCharWidth(PdbType pointerType)
    {
        if (pointerType.ReferentType != 0)
        {
            var referent = _pdb.Types.Peel(pointerType.ReferentType);
            if (referent.Kind == PdbTypeKind.Primitive &&
                referent.Name is "char" or "signed char" or "unsigned char" or "wchar_t")
                return (int)referent.ByteSize;
            return 0;
        }
        return (pointerType.TypeIndex & 0xFF) switch
        {
            0x10 or 0x20 or 0x70 => 1, // signed/unsigned/plain char
            0x71 => 2,                 // wchar_t
            _ => 0,
        };
    }

    // Reads a NUL-terminated string (charSize 1 or 2) from kit memory and returns it quoted, with
    // control characters shown as C escapes. Bounded so a bad/unterminated pointer can't run away.
    private static string ReadCString(uint address, int charSize, KitMemoryAccess memory)
    {
        const int maxChars = 512;
        var sb = new System.Text.StringBuilder("\"");
        for (var i = 0; i < maxChars; i++)
        {
            var dw = memory.ReadDword((nuint)(address + (uint)(i * charSize)));
            if (dw is null) { sb.Append('…'); break; }
            var ch = charSize == 1 ? (int)(dw.Value & 0xFF) : (int)(dw.Value & 0xFFFF);
            if (ch == 0) break;
            switch (ch)
            {
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch >= 32 ? (char)ch : '.'); break;
            }
            if (i == maxChars - 1) sb.Append('…');
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string FormatScalar(PdbType type, nuint address, KitMemoryAccess memory)
    {
        var low = memory.ReadDword(address);
        if (low is null)
            return "<unreadable>";
        var value = low.Value;

        if (type.IsFloatingPoint && type.ByteSize == 8)
        {
            var high = memory.ReadDword(address + 4) ?? 0;
            var bits = ((ulong)high << 32) | value;
            return $"{BitConverter.Int64BitsToDouble((long)bits):g}";
        }

        if (type.IsFloatingPoint)
            return $"{BitConverter.Int32BitsToSingle((int)value):g} (0x{value:x8})";

        if (type.Kind == PdbTypeKind.Pointer)
            return $"0x{value:x8}";

        if (type.ByteSize == 8)
        {
            var high = memory.ReadDword(address + 4) ?? 0;
            return $"0x{high:x8}{value:x8}";
        }

        value = type.ByteSize switch
        {
            1 => value & 0xFF,
            2 => value & 0xFFFF,
            _ => value,
        };

        var isUnsigned = type.Name is not null && type.Name.Contains("unsigned", StringComparison.Ordinal);
        return isUnsigned ? $"{value} (0x{value:x8})" : $"{(int)value} (0x{value:x8})";
    }

    // Skip compiler-generated / mangled helper locals to match the dbghelp path's filtering.
    private static bool IsHidden(string name) =>
        string.IsNullOrEmpty(name) || name.StartsWith("__", StringComparison.Ordinal);

    // Global symbol records carry more toolchain noise than locals: precompiled-header markers
    // (__@@_PchSym_), CRT section anchors (__xc_a/__xi_a), string-literal statics ($SG.../??_C),
    // and C++-mangled names. Filter those the way the map-file path filtered its own noise, so the
    // Globals pane shows the program's real variables.
    private static bool IsHiddenGlobal(string name) =>
        string.IsNullOrEmpty(name) ||
        name.StartsWith("__", StringComparison.Ordinal) ||
        name.StartsWith("$", StringComparison.Ordinal) ||
        name.StartsWith("?", StringComparison.Ordinal) ||
        name.StartsWith(".", StringComparison.Ordinal) ||
        name.Contains("_PchSym_", StringComparison.Ordinal);
}
