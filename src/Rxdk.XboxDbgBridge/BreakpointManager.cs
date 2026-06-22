using Rxdk.Xbdm;

namespace Rxdk.XboxDbgBridge;

internal sealed class BreakpointManager
{
    private const int MaxActive = 64;
    private const int MaxPending = 32;

    private readonly List<ActiveBreakpoint> _active = new();
    private readonly List<PendingBreakpoint> _pending = new();

    internal IReadOnlyList<ActiveBreakpoint> Active => _active;

    internal void QueuePending(string file, uint line)
    {
        if (string.IsNullOrWhiteSpace(file) || line == 0)
            return;
        if (_pending.Any(p => p.Line == line && string.Equals(p.File, file, StringComparison.OrdinalIgnoreCase)))
            return;
        if (_pending.Count >= MaxPending)
            return;
        _pending.Add(new PendingBreakpoint(file, line));
    }

    internal void ClearPending() => _pending.Clear();

    internal IReadOnlyList<PendingBreakpoint> Pending => _pending;

    internal InstallResult Install(
        IXbdmDebugConnection debug,
        nuint address,
        string? file,
        uint line,
        Func<nuint, nuint> normalize,
        Func<nuint, bool> isKitAddress)
    {
        if (address == 0)
            return InstallResult.InvalidAddress;

        address = normalize(address);
        if (!isKitAddress(address))
            return InstallResult.InvalidAddress;

        if (_active.Any(bp => bp.Address == address))
            return InstallResult.Success;

        try
        {
            debug.SetBreakpoint(address);
            if (_active.Count < MaxActive)
                _active.Add(new ActiveBreakpoint(address, false, file, line));
            return InstallResult.Success;
        }
        catch (XbdmException)
        {
            if (CountHardware() >= 4 && !EvictOldestHardware(debug))
                return InstallResult.HardwareFull;

            debug.SetDataBreakpoint(address, XbdmDebugConstants.DmbreakExecute, 1);
            if (_active.Count < MaxActive)
                _active.Add(new ActiveBreakpoint(address, true, file, line));
            BridgeWriter.Log($"Using hardware execute BP at 0x{address:x} (soft install failed)");
            return InstallResult.Success;
        }
    }

    internal void Remove(IXbdmDebugConnection debug, nuint address)
    {
        var bpType = debug.GetBreakpointType(address);
        if (bpType == XbdmDebugConstants.DmbreakExecute)
            debug.SetDataBreakpoint(address, XbdmDebugConstants.DmbreakNone, 1);
        else if (bpType != XbdmDebugConstants.DmbreakNone)
            debug.RemoveBreakpoint(address);
        _active.RemoveAll(bp => bp.Address == address);
    }

    internal void Clear(IXbdmDebugConnection debug)
    {
        foreach (var bp in _active.ToArray())
            Remove(debug, bp.Address);
        _active.Clear();
    }

    internal void RearmHardware(IXbdmDebugConnection debug)
    {
        foreach (var bp in _active.Where(bp => bp.Hardware))
            debug.SetDataBreakpoint(bp.Address, XbdmDebugConstants.DmbreakExecute, 1);
    }

    internal void ReapplyActiveBreakpoints(
        IXbdmDebugConnection debug,
        Func<string, uint, nuint?> resolveLine,
        Func<nuint, nuint> normalize,
        Func<nuint, bool> isKitAddress)
    {
        var withSource = _active
            .Where(bp => !string.IsNullOrWhiteSpace(bp.File) && bp.Line != 0)
            .Select(bp => (bp.File!, bp.Line))
            .Distinct()
            .ToArray();

        foreach (var bp in _active.ToArray())
            Remove(debug, bp.Address);

        foreach (var (file, line) in withSource)
        {
            var address = resolveLine(file, line);
            if (address is null or 0)
                continue;
            Install(debug, address.Value, file, line, normalize, isKitAddress);
        }
    }

    internal bool IsActive(nuint address) => _active.Any(bp => bp.Address == address);

    internal bool IsHardware(nuint address) => _active.FirstOrDefault(bp => bp.Address == address).Hardware;

    internal ActiveBreakpoint? FindAtAddress(nuint address) =>
        _active.FirstOrDefault(bp => bp.Address == address);

    internal void TrackInstalled(nuint address, bool hardware, string? file, uint line)
    {
        if (_active.Any(bp => bp.Address == address))
            return;
        if (_active.Count >= MaxActive)
            return;
        _active.Add(new ActiveBreakpoint(address, hardware, file, line));
    }

    internal void Untrack(nuint address) => _active.RemoveAll(bp => bp.Address == address);

    private int CountHardware() => _active.Count(bp => bp.Hardware);

    private bool EvictOldestHardware(IXbdmDebugConnection debug)
    {
        var victim = _active.FirstOrDefault(bp => bp.Hardware);
        if (victim.Address == 0)
            return false;
        Remove(debug, victim.Address);
        BridgeWriter.Log($"Evicted hardware BP at 0x{victim.Address:x} (Xbox allows 4 execute HW breakpoints)");
        return true;
    }

    internal readonly record struct ActiveBreakpoint(nuint Address, bool Hardware, string? File, uint Line);

    internal readonly record struct PendingBreakpoint(string File, uint Line);

    internal enum InstallResult
    {
        Success,
        InvalidAddress,
        HardwareFull,
    }
}
