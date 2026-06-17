using System.Text;
using System.Text.Json;
using Rxdk.Xbdm;
using Rxdk.XboxDbgBridge.Symbols;

namespace Rxdk.XboxDbgBridge;

internal sealed partial class DebugBridgeSession
{
    private readonly SymbolService _symbols = new();

    private void LoadSymbols(JsonElement root, int id)
    {
        if (!_symbols.IsAvailable)
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"symbolsRequireWindows\"");
            return;
        }

        if (!BridgeJson.TryGetString(root, "exe", out var exe))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing exe\"");
            return;
        }

        if (!BridgeJson.TryGetString(root, "pdb", out var pdb))
            pdb = exe;
        BridgeJson.TryGetString(root, "map", out var map);

        try
        {
            _symbols.Load(exe, pdb, string.IsNullOrEmpty(map) ? null : map);
            BridgeWriter.EmitResult(id, true);
        }
        catch (Exception ex)
        {
            BridgeWriter.EmitResult(id, false, $"\"error\":\"{Escape(ex.Message)}\"");
        }
    }

    private void ResolveLine(JsonElement root, int id)
    {
        if (!BridgeJson.TryGetString(root, "file", out var file) ||
            !BridgeJson.TryGetUInt32(root, "line", out var line) || line == 0)
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing file/line\"");
            return;
        }

        if (!_symbols.TryResolveLine(file, line, out var address))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"resolveLine\"");
            return;
        }

        BridgeWriter.EmitResult(id, true,
            $"\"address\":\"0x{address:x}\",\"moduleBase\":\"0x{_moduleBase:x}\"");
    }

    private void SymbolInfo(int id)
    {
        BridgeWriter.EmitResult(id, true,
            $"\"diag\":\"{Escape(_symbols.Diag())}\",\"moduleBase\":\"0x{_moduleBase:x}\"");
    }

    private void Diag(int id) => EmitKitDiag(id);

    private void GetVariables(JsonElement root, int id)
    {
        if (!BridgeJson.TryGetString(root, "scope", out var scope))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing scope\"");
            return;
        }

        EnsureNotifications();
        var threadId = _stoppedThread != 0 ? _stoppedThread : _mainThread;
        if (BridgeJson.TryGetUInt32(root, "threadId", out var requested))
            threadId = requested;

        var variables = new VariableJson();
        if (scope.Equals("registers", StringComparison.OrdinalIgnoreCase))
        {
            var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl | XbdmDebugConstants.ContextInteger };
            _debug!.GetThreadContext(threadId, ref context);
            _symbols.EmitRegisters(variables, ref context);
        }
        else if (scope.Equals("globals", StringComparison.OrdinalIgnoreCase))
        {
            _symbols.EmitGlobals(variables, maxVars: 256);
        }
        else if (scope.Equals("locals", StringComparison.OrdinalIgnoreCase))
        {
            if (!_symbols.IsAvailable)
            {
                BridgeWriter.EmitResult(id, false, "\"error\":\"symbolsRequireWindows\"");
                return;
            }

            var context = new XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl | XbdmDebugConstants.ContextInteger };
            _debug!.GetThreadContext(threadId, ref context);
            _symbols.EmitLocals(ref context, variables, CreateKitMemory());
        }
        else
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"unknown scope\"");
            return;
        }

        BridgeWriter.Emit(
            $"{{\"type\":\"result\",\"id\":{id},\"success\":true,\"variables\":[{variables.ToJsonArray()}]}}");
    }

    private void Evaluate(JsonElement root, int id)
    {
        if (!BridgeJson.TryGetString(root, "expression", out var expression))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing expression\"");
            return;
        }

        EnsureNotifications();
        var threadId = _stoppedThread != 0 ? _stoppedThread : _mainThread;
        if (BridgeJson.TryGetUInt32(root, "threadId", out var requested))
            threadId = requested;

        var context = new Xbdm.XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl | XbdmDebugConstants.ContextInteger };
        _debug!.GetThreadContext(threadId, ref context);

        if (!_symbols.TryEvaluate(expression, ref context, CreateKitMemory(), out var value, out var error))
        {
            BridgeWriter.EmitResult(id, false, $"\"error\":\"{error ?? "evaluate"}\"");
            return;
        }

        var extra = new StringBuilder("\"value\":");
        BridgeJsonWriter.AppendEscaped(extra, value);
        BridgeWriter.EmitResult(id, true, extra.ToString());
    }

    private void GetMembers(JsonElement root, int id)
    {
        if (!BridgeJson.TryGetString(root, "base", out var symbolBase) &&
            !BridgeJson.TryGetString(root, "name", out symbolBase))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"missing base\"");
            return;
        }

        EnsureNotifications();
        var threadId = _stoppedThread != 0 ? _stoppedThread : _mainThread;
        if (BridgeJson.TryGetUInt32(root, "threadId", out var requested))
            threadId = requested;

        var context = new Xbdm.XbdmContext { ContextFlags = XbdmDebugConstants.ContextControl | XbdmDebugConstants.ContextInteger };
        _debug!.GetThreadContext(threadId, ref context);
        var variables = new VariableJson();
        if (!_symbols.TryEmitMembers(symbolBase, ref context, CreateKitMemory(), variables))
        {
            BridgeWriter.EmitResult(id, false, "\"error\":\"memberNotFound\"");
            return;
        }

        BridgeWriter.Emit(
            $"{{\"type\":\"result\",\"id\":{id},\"success\":true,\"variables\":[{variables.ToJsonArray()}]}}");
    }

    private uint? ReadDword(nuint address)
    {
        if (_debug is null)
            return null;
        Span<byte> buffer = stackalloc byte[4];
        if (_debug.GetMemory(address, buffer) != 4)
            return null;
        return BitConverter.ToUInt32(buffer);
    }

    private byte? ReadByte(nuint address)
    {
        if (_debug is null)
            return null;
        Span<byte> buffer = stackalloc byte[1];
        if (_debug.GetMemory(address, buffer) != 1)
            return null;
        return buffer[0];
    }

    private KitMemoryAccess CreateKitMemory() => new()
    {
        ReadDword = ReadDword,
        ReadByte = ReadByte,
    };

    private void OnModuleBaseSet()
    {
        _symbols.ModuleBase = _moduleBase;
    }

    private void ApplyPendingBreakpoints()
    {
        if (_debug is null || _moduleBase == 0)
            return;

        foreach (var pending in _breakpoints.Pending.ToArray())
        {
            if (!_symbols.TryResolveLine(pending.File, pending.Line, out var address))
                continue;
            _breakpoints.Install(_debug, address, pending.File, pending.Line, NormalizeBpAddress, IsKitBpAddress);
        }

        _breakpoints.ClearPending();
    }

    private nuint NormalizeBpAddress(nuint address) => _symbols.NormalizeBreakpointAddress(address);

    private bool IsKitBpAddress(nuint address) => _symbols.IsKitBreakpointAddress(address);
}
