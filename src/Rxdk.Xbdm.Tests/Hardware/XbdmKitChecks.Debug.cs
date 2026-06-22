using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private static KitCheckResult CompareThreadList(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            DebugCategory,
            "GetThreadList",
            () => session.ManagedDebug.GetThreadList().OrderBy(id => id).ToArray(),
            threads => $"{threads.Length} threads");

    private static KitCheckResult CompareLoadedModules(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            DebugCategory,
            "WalkLoadedModules",
            () => session.ManagedDebug.WalkLoadedModules(),
            modules => $"{modules.Count} modules");

    private static KitCheckResult CompareSystemTime(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            DebugCategory,
            "GetSystemTime",
            () =>
            {
                var kitTime = session.ManagedDebug.GetSystemTime();
                var delta = Math.Abs((kitTime - DateTime.UtcNow).TotalSeconds);
                if (delta > 5)
                    throw XbdmException.FromHResult($"Clock delta {delta:F1}s too large.", XbdmHResults.FileError);
                return delta;
            },
            delta => $"delta={delta:F1}s");

    private static KitCheckResult CompareXtlData(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            DebugCategory,
            "GetXtlData",
            () => session.ManagedDebug.GetXtlData(),
            data => $"offset=0x{data.LastErrorOffset:x}");

    private static KitCheckResult CompareSendCommandSetsystime(XbdmKitSession session)
    {
        var ft = DateTime.UtcNow.ToFileTimeUtc();
        var high = (uint)(ft >> 32);
        var low = (uint)ft;
        var command = $"setsystime clockhi=0x{high:x8} clocklo=0x{low:x8}";

        return KitCheck.ManagedCheck(
            DebugCategory,
            "SendCommand(setsystime)",
            () => session.ManagedDebug.SendCommand(command));
    }

    private static KitCheckResult CompareNotificationSession(XbdmKitSession session)
    {
        try
        {
            using var notify = session.ManagedDebug.OpenNotificationSession(XbdmDebugConstants.DmPersistent);
            return KitCheck.Pass(DebugCategory, "OpenNotificationSession", "opened");
        }
        catch (XbdmException ex)
        {
            return KitCheck.Fail(DebugCategory, "OpenNotificationSession", ex.Message);
        }
    }

    private static KitCheckResult ComparePerformanceCounterList(XbdmKitSession session) =>
        KitCheck.ManagedCheck(
            DebugCategory,
            "WalkPerformanceCounters",
            () => session.ManagedDebug.WalkPerformanceCounters().Select(c => c.Name).OrderBy(n => n).ToArray(),
            names => $"{names.Length} counters");

    private static KitCheckResult CompareMemoryAtModuleBase(XbdmKitSession session)
    {
        var module = session.ManagedDebug.WalkLoadedModules().FirstOrDefault(m => m.BaseAddress != 0);
        if (module is null)
            return KitCheck.Skip(DebugCategory, "GetMemory(module)", "No loaded modules.");

        Span<byte> buffer = stackalloc byte[16];
        var read = session.ManagedDebug.GetMemory(module.BaseAddress, buffer);
        return read > 0
            ? KitCheck.Pass(DebugCategory, "GetMemory(module)", $"0x{module.BaseAddress:x} {read} bytes")
            : KitCheck.Fail(DebugCategory, "GetMemory(module)", "No bytes read.");
    }

    private static KitCheckResult CompareBreakpointQuery(XbdmKitSession session)
    {
        var module = session.ManagedDebug.WalkLoadedModules().FirstOrDefault(m => m.BaseAddress != 0);
        if (module is null)
            return KitCheck.Skip(DebugCategory, "GetBreakpointType", "No loaded modules.");

        return KitCheck.ManagedCheck(
            DebugCategory,
            "GetBreakpointType(unset)",
            () => session.ManagedDebug.GetBreakpointType(module.BaseAddress),
            type => $"0x{module.BaseAddress:x} type={type}");
    }
}
