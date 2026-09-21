using System.Text;
using Rxdk.Dwarf;

namespace Rxdk.XboxDbgBridge.Symbols;

/// <summary>
/// Resolves debug symbols (line mapping, locals, globals, watch/hover expressions) for the loaded
/// title. There is a single, toolchain- and OS-agnostic code path: DWARF read straight from the
/// title's .exe, which the RXDK clang build emits (there is no .pdb and no linker .map anymore).
/// The same reader and type/expression engine run on Windows, Linux and macOS.
/// </summary>
internal sealed class SymbolService : IDisposable
{
    private bool _loaded;
    private nuint _moduleBase;

    // DWARF read straight from the title's .exe. The RXDK linker bases the image at 0x10000, so DWARF
    // addresses are relative to that; the whole service (breakpoint relocation, kit<->image math)
    // keys off this base.
    private string _imagePath = string.Empty;
    private DwarfInfo? _dwarf;
    private bool _dwarfTried;
    private const ulong ImageBase = 0x10000;

    // Symbols are available on every platform: the reader has no OS dependency.
    internal bool IsAvailable => true;

    internal nuint ModuleBase
    {
        get => _moduleBase;
        set => _moduleBase = value;
    }

    internal void Load(string exePath) => LoadModule(exePath);

    internal void LoadFromXbe(string xbePath) => LoadModule(xbePath);

    private void LoadModule(string imagePath)
    {
        Unload();
        _loaded = true;
        _imagePath = imagePath;
        _dwarf = null;
        _dwarfTried = false;
    }

    internal void Unload()
    {
        _loaded = false;
        _moduleBase = 0;
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
        return info is null ? null : new DwarfSymbols(info, _moduleBase, ImageBase);
    }

    internal nuint RelocateAddress(nuint imageAddress)
    {
        if (_moduleBase == 0)
            return imageAddress;
        return _moduleBase + (imageAddress - (nuint)ImageBase);
    }

    internal nuint NormalizeBreakpointAddress(nuint address)
    {
        if (address == 0 || _moduleBase == 0)
            return address;

        var imageBase = (nuint)ImageBase;
        if (address >= imageBase && address < imageBase + 0x100000 &&
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
        return false;
    }

    internal bool TryAddressToLine(nuint kitAddress, out string file, out uint line, out string function)
    {
        file = string.Empty;
        line = 0;
        function = string.Empty;
        if (!_loaded)
            return false;

        var dwarf = TryGetDwarf();
        return dwarf is not null && dwarf.TryAddressToLine((uint)kitAddress, out file, out line, out function);
    }

    internal string Diag()
    {
        var info = TryGetDwarfInfo();
        var diag = new StringBuilder();
        diag.Append($"imageBase=0x{ImageBase:x} symType=dwarf moduleBase=0x{_moduleBase:x}");
        if (info is not null)
        {
            int units = info.Units.Count, funcs = 0, lines = 0;
            foreach (var u in info.Units) { funcs += u.Functions.Count; lines += u.Lines.Count; }
            diag.Append($" units={units} funcs={funcs} lines={lines}");
        }
        else
        {
            diag.Append(" dwarf=none");
        }
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
        if (dwarf is null)
        {
            error = "symbolsNotLoaded";
            return false;
        }

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

    internal void EmitLocals(ref Xbdm.XbdmContext context, VariableJson variables, KitMemoryAccess memory)
    {
        if (!_loaded)
            return;

        var dwarf = TryGetDwarf();
        if (dwarf is null)
            return;

        try { dwarf.EmitLocals(ref context, variables, memory); }
        catch (Exception ex) { BridgeWriter.Log($"DWARF EmitLocals failed: {ex.Message}"); }
    }

    internal bool TryEmitMembers(string symbolBase, ref Xbdm.XbdmContext context, KitMemoryAccess memory, VariableJson variables)
    {
        if (!_loaded)
            return false;

        var dwarf = TryGetDwarf();
        if (dwarf is null)
            return false;

        try { return dwarf.TryEmitMembers(symbolBase, ref context, variables, memory); }
        catch (Exception ex) { BridgeWriter.Log($"DWARF TryEmitMembers failed: {ex.Message}"); return false; }
    }

    internal void EmitGlobals(VariableJson variables, KitMemoryAccess memory, int maxVars)
    {
        if (!_loaded)
            return;

        // DWARF carries only the title's own compile units, so the globals are the program's own (a
        // small set) rather than the flood of library globals a .pdb enumerated -- which is why the
        // old visibility toggle is gone. Types come with the DIEs, so aggregates expand like locals.
        var dwarf = TryGetDwarf();
        if (dwarf is null)
            return;

        try { dwarf.EmitGlobals(variables, memory, maxVars); }
        catch (Exception ex) { BridgeWriter.Log($"DWARF EmitGlobals failed: {ex.Message}"); }
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
