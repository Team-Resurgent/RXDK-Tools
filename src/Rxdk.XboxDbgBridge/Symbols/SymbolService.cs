using System.Text;
using Rxdk.Pdb;
using Rxdk.Dwarf;

namespace Rxdk.XboxDbgBridge.Symbols;

/// <summary>
/// Resolves debug symbols (line mapping, locals, globals, watch/hover expressions) for the loaded
/// title. There is a single, toolchain- and OS-agnostic code path: the pure-managed
/// <see cref="PdbImage"/> reader. dbghelp is gone, so "works on Windows" now implies "works on
/// Linux/macOS" — the same PDB parser and type/expression engine run everywhere.
/// </summary>
internal sealed class SymbolService : IDisposable
{
    private bool _loaded;
    private ulong _pdbBase;
    private nuint _moduleBase;
    private string _mapPath = string.Empty;
    private uint _mapLinkBase = 0x400000;

    // Pure-managed PDB reader (opened lazily from the loaded PDB path) plus a cached availability
    // flag so a bad/unreadable PDB is not retried on every request.
    private string _pdbPath = string.Empty;
    private PdbImage? _pdbImage;
    private bool _managedUnavailable;

    // DWARF read straight from the title's .exe (the RXDK clang build emits DWARF, not a .pdb). This is
    // the primary symbol source now; the PDB path above stays as a fallback for older/.pdb titles. The
    // RXDK linker bases the image at 0x10000, so DWARF addresses are relative to that.
    private string _imagePath = string.Empty;
    private DwarfInfo? _dwarf;
    private bool _dwarfTried;
    private const ulong DwarfImageBase = 0x10000;

    // Symbols are available on every platform: the managed reader has no OS dependency.
    internal bool IsAvailable => true;

    internal ulong PdbBase => _pdbBase;

    internal nuint ModuleBase
    {
        get => _moduleBase;
        set => _moduleBase = value;
    }

    internal void Load(string exePath, string pdbPath, string? mapPath)
        => LoadModule(exePath, pdbPath, mapPath);

    internal void LoadFromXbe(string xbePath, string pdbPath, string? mapPath)
        => LoadModule(xbePath, pdbPath, mapPath);

    private void LoadModule(string imagePath, string pdbPath, string? mapPath)
    {
        Unload();

        // The managed reader works in image RVAs relocated to the kit module base. Use the PDB's link
        // base (0x400000 for original-Xbox titles) as the reference the rest of the service speaks.
        _loaded = true;
        _pdbPath = pdbPath;
        _pdbImage = null;
        _managedUnavailable = false;
        _imagePath = imagePath;
        _dwarf = null;
        _dwarfTried = false;

        // The link base the symbol addresses are relative to, which the whole service (breakpoint
        // relocation, kit<->image address math) keys off. RXDK's clang links at 0x10000, so a DWARF
        // title uses that; a legacy MSVC .pdb title uses 0x400000. Decide up front by probing DWARF.
        _pdbBase = TryGetDwarfInfo() is not null ? DwarfImageBase : 0x400000;

        var map = string.IsNullOrWhiteSpace(mapPath)
            ? Path.ChangeExtension(imagePath, ".map")
            : mapPath;
        if (File.Exists(map))
        {
            _mapPath = map;
            _mapLinkBase = MapFileGlobals.ReadLinkBase(map) ?? 0x400000;
        }
        else
        {
            _mapPath = string.Empty;
            _mapLinkBase = 0x400000;
        }
    }

    internal void Unload()
    {
        _loaded = false;
        _pdbBase = 0;
        _moduleBase = 0;
        _mapPath = string.Empty;
        _mapLinkBase = 0x400000;
        _pdbPath = string.Empty;
        _pdbImage = null;
        _managedUnavailable = false;
        _imagePath = string.Empty;
        _dwarf = null;
        _dwarfTried = false;
    }

    /// <summary>The DWARF read from the loaded .exe, opened once; null if the image has no DWARF.</summary>
    private DwarfInfo? TryGetDwarfInfo()
    {
        if (_dwarfTried) return _dwarf;
        _dwarfTried = true;
        if (string.IsNullOrEmpty(_imagePath) || !File.Exists(_imagePath)) return null;
        try
        {
            var info = DwarfReader.Read(_imagePath);
            bool hasContent = false;
            foreach (var _ in info.Functions) { hasContent = true; break; }
            if (!hasContent) foreach (var u in info.Units) if (u.Lines.Count > 0) { hasContent = true; break; }
            _dwarf = hasContent ? info : null;
        }
        catch (Exception ex)
        {
            BridgeWriter.Log($"DWARF read failed ({_imagePath}): {ex.Message}");
            _dwarf = null;
        }
        return _dwarf;
    }

    /// <summary>A DWARF symbol reader over the loaded image once the kit module base is known.</summary>
    private DwarfSymbols? TryGetDwarf()
    {
        var info = TryGetDwarfInfo();
        return info is null ? null : new DwarfSymbols(info, _moduleBase, DwarfImageBase);
    }

    /// <summary>Opens (once) the managed PDB reader over the loaded PDB path, or null if unavailable.</summary>
    private PdbImage? TryGetPdbImage()
    {
        if (_managedUnavailable || string.IsNullOrEmpty(_pdbPath))
            return null;

        if (_pdbImage is null)
        {
            try
            {
                _pdbImage = PdbImage.OpenFile(_pdbPath);
            }
            catch (Exception ex)
            {
                _managedUnavailable = true;
                BridgeWriter.Log($"managed PDB open failed ({_pdbPath}): {ex.Message}");
                return null;
            }
        }

        return _pdbImage;
    }

    /// <summary>Builds a managed symbol reader over the loaded PDB, or null if it can't be used yet.</summary>
    private ManagedSymbols? TryGetManaged()
    {
        if (_moduleBase == 0)
            return null;

        var pdb = TryGetPdbImage();
        return pdb is null ? null : new ManagedSymbols(pdb, _moduleBase);
    }

    internal nuint RelocateAddress(nuint pdbAddress)
    {
        if (_moduleBase == 0 || _pdbBase == 0)
            return pdbAddress;
        return _moduleBase + (pdbAddress - (nuint)_pdbBase);
    }

    internal nuint NormalizeBreakpointAddress(nuint address)
    {
        if (address == 0 || _moduleBase == 0 || _pdbBase == 0)
            return address;

        var pdbBase = (nuint)_pdbBase;
        if (address >= pdbBase && address < pdbBase + 0x100000 &&
            (address < _moduleBase || address >= _moduleBase + 0x100000))
            return RelocateAddress(address);
        return address;
    }

    internal bool IsKitBreakpointAddress(nuint address)
    {
        if (address == 0)
            return false;
        if (_moduleBase != 0)
            return address >= _moduleBase && address < _moduleBase + 0x01000000;
        return address < 0x00400000 || address >= 0x00600000;
    }

    // The reader works in image RVAs; convert to the PDB link-base address the rest of the service
    // speaks, then relocate to the kit module base.
    internal bool TryResolveLine(string file, uint line, out nuint address)
    {
        address = 0;
        if (!_loaded)
            return false;

        var dwarf = TryGetDwarf();
        if (dwarf is not null && dwarf.TryResolveLine(file, line, out var kitAddr))
        {
            address = (nuint)kitAddr;
            return true;
        }

        if (_pdbBase == 0)
            return false;

        var pdb = TryGetPdbImage();
        if (pdb is null || !pdb.TryResolveLine(file, line, out var rva))
            return false;

        var pdbAddr = (nuint)(_pdbBase + rva);
        address = _moduleBase != 0 ? RelocateAddress(pdbAddr) : pdbAddr;
        return true;
    }

    internal bool TryAddressToLine(nuint kitAddress, out string file, out uint line, out string function)
    {
        file = string.Empty;
        line = 0;
        function = string.Empty;
        if (!_loaded)
            return false;

        var dwarf = TryGetDwarf();
        if (dwarf is not null && dwarf.TryAddressToLine((uint)kitAddress, out file, out line, out function))
            return true;

        var pdb = TryGetPdbImage();
        if (pdb is null)
            return false;

        // Kit runtime address -> image RVA.
        nuint moduleBase = _moduleBase != 0 ? _moduleBase : (nuint)_pdbBase;
        if (kitAddress < moduleBase)
            return false;
        var rva = (uint)(kitAddress - moduleBase);

        if (pdb.TryFindFunctionName(rva, out var fn))
            function = fn;

        return pdb.TryFindLine(rva, out file, out line);
    }

    internal string Diag()
    {
        var pdb = TryGetPdbImage();
        var diag = new StringBuilder();
        diag.Append($"pdbBase=0x{_pdbBase:x} symType=managed moduleBase=0x{_moduleBase:x}");
        if (pdb is not null)
            diag.Append($" lines={pdb.Lines.Entries.Count}");
        if (pdb is not null && pdb.TryFindSymbolRva("_main", out var mainRva))
            diag.Append($" _main=0x{_pdbBase + mainRva:x}");
        return diag.ToString();
    }

    internal bool TryEvaluate(string expression, ref Xbdm.XbdmContext context, KitMemoryAccess memory, out string value, out string? error, out bool expandable)
    {
        value = string.Empty;
        error = null;
        expandable = false;
        if (!_loaded)
        {
            error = "symbolsNotLoaded";
            return false;
        }

        var dwarf = TryGetDwarf();
        if (dwarf is not null)
        {
            try
            {
                return dwarf.TryEvaluate(expression, ref context, memory, out value, out error, out expandable);
            }
            catch (Exception ex)
            {
                BridgeWriter.Log($"DWARF TryEvaluate failed: {ex.Message}");
                error = "evaluate";
                return false;
            }
        }

        if (_pdbBase == 0)
        {
            error = "symbolsNotLoaded";
            return false;
        }

        var managed = TryGetManaged();
        if (managed is null)
        {
            error = "symbolsNotLoaded";
            return false;
        }

        try
        {
            return managed.TryEvaluate(expression, ref context, memory, out value, out error, out expandable);
        }
        catch (Exception ex)
        {
            BridgeWriter.Log($"managed TryEvaluate failed: {ex.Message}");
            error = "evaluate";
            return false;
        }
    }

    internal void EmitLocals(ref Xbdm.XbdmContext context, VariableJson variables, KitMemoryAccess memory)
    {
        if (!_loaded)
            return;

        var dwarf = TryGetDwarf();
        if (dwarf is not null)
        {
            try { dwarf.EmitLocals(ref context, variables, memory); }
            catch (Exception ex) { BridgeWriter.Log($"DWARF EmitLocals failed: {ex.Message}"); }
            return;
        }

        if (_pdbBase == 0)
            return;

        var managed = TryGetManaged();
        if (managed is null)
            return;

        try
        {
            managed.EmitLocals(ref context, variables, memory);
        }
        catch (Exception ex)
        {
            BridgeWriter.Log($"managed EmitLocals failed: {ex.Message}");
        }
    }

    internal bool TryEmitMembers(string symbolBase, ref Xbdm.XbdmContext context, KitMemoryAccess memory, VariableJson variables)
    {
        if (!_loaded)
            return false;

        var dwarf = TryGetDwarf();
        if (dwarf is not null)
        {
            try { return dwarf.TryEmitMembers(symbolBase, ref context, variables, memory); }
            catch (Exception ex) { BridgeWriter.Log($"DWARF TryEmitMembers failed: {ex.Message}"); return false; }
        }

        if (_pdbBase == 0)
            return false;

        var managed = TryGetManaged();
        if (managed is null)
            return false;

        try
        {
            return managed.TryEmitMembers(symbolBase, ref context, variables, memory);
        }
        catch (Exception ex)
        {
            BridgeWriter.Log($"managed TryEmitMembers failed: {ex.Message}");
            return false;
        }
    }

    internal void EmitGlobals(VariableJson variables, KitMemoryAccess memory, int maxVars)
    {
        if (!_loaded)
            return;

        // Prefer DWARF: it carries only the title's own compile units, so the globals are the
        // program's own (a small set) rather than the flood of library globals a .pdb enumerates --
        // which is why the old visibility toggle is gone. Types come with the DIEs, so aggregates
        // format and expand like locals.
        var dwarf = TryGetDwarf();
        if (dwarf is not null)
        {
            try { if (dwarf.EmitGlobals(variables, memory, maxVars)) return; }
            catch (Exception ex) { BridgeWriter.Log($"DWARF EmitGlobals failed: {ex.Message}"); }
            return;
        }

        // Legacy .pdb path: enumerate the global-symbol stream with real type info, then fall back to
        // the linker .map only when the PDB yields nothing (no symbols, or moduleBase not yet known).
        var managed = TryGetManaged();
        if (managed is not null)
        {
            try
            {
                // Show every tier (2): the DWARF path is the norm now; this legacy branch only runs
                // for an old .pdb build, where showing all globals is the least-surprising default.
                if (managed.EmitGlobals(variables, memory, maxVars, maxTier: 2))
                    return;
            }
            catch (Exception ex)
            {
                BridgeWriter.Log($"managed EmitGlobals failed: {ex.Message}");
            }
        }

        if (!string.IsNullOrEmpty(_mapPath))
            MapFileGlobals.Emit(_mapPath, _mapLinkBase, _moduleBase, variables, maxVars);
    }

    internal void EmitRegisters(VariableJson variables, ref Xbdm.XbdmContext context)
    {
        variables.Append("EAX", $"0x{context.Eax:x8}");
        variables.Append("EBX", $"0x{context.Ebx:x8}");
        variables.Append("ECX", $"0x{context.Ecx:x8}");
        variables.Append("EDX", $"0x{context.Edx:x8}");
        variables.Append("ESI", $"0x{context.Esi:x8}");
        variables.Append("EDI", $"0x{context.Edi:x8}");
        variables.Append("EBP", $"0x{context.Ebp:x8}");
        variables.Append("ESP", $"0x{context.Esp:x8}");
        variables.Append("EIP", $"0x{context.Eip:x8}");
        variables.Append("EFLAGS", $"0x{context.EFlags:x8}");
    }

    public void Dispose() => Unload();
}
