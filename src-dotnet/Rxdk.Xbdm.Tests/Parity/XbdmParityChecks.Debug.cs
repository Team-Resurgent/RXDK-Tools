using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Parity;

internal static partial class XbdmParityChecks
{
    private static ParityCheckResult CompareThreadList(XbdmParitySession session)
    {
        var native = session.NativeDebug.GetThreadList().OrderBy(id => id).ToArray();
        var managed = session.ManagedDebug.GetThreadList().OrderBy(id => id).ToArray();
        if (native.SequenceEqual(managed))
            return ParityCompare.Pass(DebugCategory, "GetThreadList", $"{native.Length} threads");

        return ParityCompare.Fail(
            DebugCategory,
            "GetThreadList",
            "Thread lists differ.",
            string.Join(',', native),
            string.Join(',', managed));
    }

    private static ParityCheckResult CompareLoadedModules(XbdmParitySession session)
    {
        string Format(IReadOnlyList<XbdmModLoadNotification> modules) =>
            string.Join(';', modules.OrderBy(m => m.Name).Select(m => $"{m.Name}@0x{m.BaseAddress:x}"));

        var native = session.NativeDebug.WalkLoadedModules();
        var managed = session.ManagedDebug.WalkLoadedModules();
        var nativeText = Format(native);
        var managedText = Format(managed);
        if (nativeText == managedText)
            return ParityCompare.Pass(DebugCategory, "WalkLoadedModules", $"{native.Count} modules");

        return ParityCompare.Fail(
            DebugCategory,
            "WalkLoadedModules",
            "Module lists differ.",
            nativeText,
            managedText);
    }

    private static ParityCheckResult CompareSystemTime(XbdmParitySession session)
    {
        var native = session.NativeDebug.GetSystemTime();
        var managed = session.ManagedDebug.GetSystemTime();
        var delta = Math.Abs((native - managed).TotalSeconds);
        if (delta <= 5)
            return ParityCompare.Pass(DebugCategory, "GetSystemTime", $"delta={delta:F1}s");

        return ParityCompare.Fail(
            DebugCategory,
            "GetSystemTime",
            "Clock delta too large.",
            native.ToString("O"),
            managed.ToString("O"));
    }

    private static ParityCheckResult CompareXtlData(XbdmParitySession session)
    {
        var native = session.NativeDebug.GetXtlData();
        var managed = session.ManagedDebug.GetXtlData();
        return native.LastErrorOffset == managed.LastErrorOffset
            ? ParityCompare.Pass(DebugCategory, "GetXtlData", $"offset=0x{native.LastErrorOffset:x}")
            : ParityCompare.Fail(
                DebugCategory,
                "GetXtlData",
                "LastErrorOffset differs.",
                $"0x{native.LastErrorOffset:x}",
                $"0x{managed.LastErrorOffset:x}");
    }

    private static ParityCheckResult CompareSendCommandSetsystime(XbdmParitySession session)
    {
        // DEBUGNAME is handled on the file connection (see GetNameOfXbox); the shared debug
        // SCI rejects it with InvalidCmd. setsystime is a supported one-line debug command.
        var ft = DateTime.UtcNow.ToFileTimeUtc();
        var high = (uint)(ft >> 32);
        var low = (uint)ft;
        var command = $"setsystime clockhi=0x{high:x8} clocklo=0x{low:x8}";

        return ParityCompare.RequireSuccessOrEqual(
            DebugCategory,
            "SendCommand(setsystime)",
            () => session.NativeDebug.SendCommand(command),
            () => session.ManagedDebug.SendCommand(command));
    }

    private static ParityCheckResult CompareNotificationSession(XbdmParitySession session)
    {
        IXbdmNotificationSession? nativeSession = null;
        IXbdmNotificationSession? managedSession = null;
        try
        {
            nativeSession = session.NativeDebug.OpenNotificationSession(XbdmDebugConstants.DmPersistent);
            managedSession = session.ManagedDebug.OpenNotificationSession(XbdmDebugConstants.DmPersistent);
            return ParityCompare.Pass(DebugCategory, "OpenNotificationSession", "opened both");
        }
        catch (XbdmException ex)
        {
            return ParityCompare.Fail(DebugCategory, "OpenNotificationSession", ex.Message);
        }
        finally
        {
            nativeSession?.Dispose();
            managedSession?.Dispose();
        }
    }

    private static ParityCheckResult ComparePerformanceCounterList(XbdmParitySession session)
    {
        var native = session.NativeDebug.WalkPerformanceCounters().Select(c => c.Name).OrderBy(n => n).ToArray();
        var managed = session.ManagedDebug.WalkPerformanceCounters().Select(c => c.Name).OrderBy(n => n).ToArray();
        if (native.SequenceEqual(managed, StringComparer.OrdinalIgnoreCase))
            return ParityCompare.Pass(DebugCategory, "WalkPerformanceCounters", $"{native.Length} counters");

        return ParityCompare.Fail(
            DebugCategory,
            "WalkPerformanceCounters",
            "Counter names differ.",
            string.Join(',', native),
            string.Join(',', managed));
    }

    private static ParityCheckResult CompareMemoryAtModuleBase(XbdmParitySession session)
    {
        var nativeModule = session.NativeDebug.WalkLoadedModules().FirstOrDefault(m => m.BaseAddress != 0);
        var managedModule = session.ManagedDebug.WalkLoadedModules().FirstOrDefault(m => m.BaseAddress != 0);
        if (nativeModule is null || managedModule is null)
            return ParityCompare.Skip(DebugCategory, "GetMemory(module)", "No loaded modules.");

        if (nativeModule.BaseAddress != managedModule.BaseAddress)
        {
            return ParityCompare.Fail(
                DebugCategory,
                "GetMemory(module)",
                "Module bases differ.",
                $"0x{nativeModule.BaseAddress:x}",
                $"0x{managedModule.BaseAddress:x}");
        }

        Span<byte> nativeBuf = stackalloc byte[16];
        Span<byte> managedBuf = stackalloc byte[16];
        var nativeRead = session.NativeDebug.GetMemory(nativeModule.BaseAddress, nativeBuf);
        var managedRead = session.ManagedDebug.GetMemory(managedModule.BaseAddress, managedBuf);
        if (nativeRead != managedRead)
        {
            return ParityCompare.Fail(
                DebugCategory,
                "GetMemory(module)",
                "Bytes read differ.",
                nativeRead.ToString(),
                managedRead.ToString());
        }

        if (!nativeBuf.SequenceEqual(managedBuf))
        {
            return ParityCompare.Fail(
                DebugCategory,
                "GetMemory(module)",
                "Memory contents differ.",
                Convert.ToHexString(nativeBuf),
                Convert.ToHexString(managedBuf));
        }

        return ParityCompare.Pass(DebugCategory, "GetMemory(module)", $"0x{nativeModule.BaseAddress:x} {nativeRead} bytes");
    }

    private static ParityCheckResult CompareBreakpointQuery(XbdmParitySession session)
    {
        var module = session.NativeDebug.WalkLoadedModules().FirstOrDefault(m => m.BaseAddress != 0);
        if (module is null)
            return ParityCompare.Skip(DebugCategory, "GetBreakpointType", "No loaded modules.");

        var address = module.BaseAddress;
        var nativeType = session.NativeDebug.GetBreakpointType(address);
        var managedType = session.ManagedDebug.GetBreakpointType(address);
        return ParityCompare.Equal(
            DebugCategory,
            "GetBreakpointType(unset)",
            nativeType,
            managedType,
            $"0x{address:x}");
    }

}
