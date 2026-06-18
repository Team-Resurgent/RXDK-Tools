using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Parity;

internal static class XbdmParityWait
{
    internal static TimeSpan LaunchTimeout =>
        TimeSpan.FromSeconds(ReadTimeoutSeconds("RXDK_PARITY_LAUNCH_TIMEOUT_SEC", 90));

    internal static TimeSpan TitleDwell =>
        TimeSpan.FromSeconds(ReadTimeoutSeconds("RXDK_PARITY_TITLE_DWELL_SEC", 5));

    /// <summary>
    /// Length of a single "watch" segment (run / frozen / resumed). When RXDK_PARITY_PAUSE is set
    /// these are stretched to 5s so the spinning-then-frozen-then-spinning transition is clearly
    /// visible; otherwise they stay short so automated runs finish quickly.
    /// </summary>
    internal static TimeSpan VisibleSegment =>
        Environment.GetEnvironmentVariable("RXDK_PARITY_PAUSE") is "1" or "true"
            ? TimeSpan.FromSeconds(ReadTimeoutSeconds("RXDK_PARITY_FREEZE_SEC", 5))
            : TimeSpan.FromSeconds(1);

    private static int ReadTimeoutSeconds(string name, int defaultSeconds)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var seconds) && seconds > 0 ? seconds : defaultSeconds;
    }

    public static bool Until(
        Func<bool> predicate,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        string? progressLabel = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        var deadline = DateTime.UtcNow + timeout;
        var nextLog = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (predicate())
                    return true;
            }
            catch (Exception ex)
            {
                if (DateTime.UtcNow >= nextLog)
                {
                    ParityProgress.Phase($"{progressLabel ?? "wait"}: {ex.Message}");
                    nextLog = DateTime.UtcNow.AddSeconds(5);
                }
            }

            if (progressLabel is not null && DateTime.UtcNow >= nextLog)
            {
                var remaining = Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalSeconds);
                ParityProgress.Phase($"{progressLabel}… {remaining}s remaining");
                nextLog = DateTime.UtcNow.AddSeconds(5);
            }

            Thread.Sleep(interval);
        }

        try
        {
            return predicate();
        }
        catch
        {
            return false;
        }
    }

    public static bool ForTitleModule(
        IXbdmDebugConnection debug,
        TimeSpan timeout,
        out XbdmModLoadNotification? module)
    {
        XbdmModLoadNotification? found = null;
        var ok = Until(
            () =>
            {
                found = debug.WalkLoadedModules().FirstOrDefault(IsTriangleModule);
                return found is not null;
            },
            timeout,
            progressLabel: "Waiting for TriangleXDK module");

        module = found;
        return ok;
    }

    public static void DwellWithProgress(string label, TimeSpan? duration = null)
    {
        var dwell = duration ?? TitleDwell;
        if (dwell <= TimeSpan.Zero)
            return;

        ParityProgress.Phase($"{label} for {dwell.TotalSeconds:F0}s");
        var deadline = DateTime.UtcNow + dwell;
        var nextLog = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            if (DateTime.UtcNow >= nextLog)
            {
                var remaining = Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalSeconds);
                ParityProgress.Phase($"{label}… {remaining}s remaining");
                nextLog = DateTime.UtcNow.AddSeconds(1);
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>
    /// When RXDK_PARITY_PAUSE is set and a console is attached, block until the user presses Enter
    /// so the current kit state can be inspected manually. No-ops in redirected/CI runs.
    /// When <paramref name="session"/> is set with <paramref name="releaseKitForNeighborhood"/>, drops
    /// parity connections so Neighborhood can open the kit (single-session limit), then reconnects managed.
    /// </summary>
    public static void PauseForUser(
        string label,
        XbdmParitySession? session = null,
        bool releaseKitForNeighborhood = false,
        string? adminPasswordHint = null)
    {
        if (Environment.GetEnvironmentVariable("RXDK_PARITY_PAUSE") is not ("1" or "true"))
            return;
        if (Console.IsInputRedirected)
            return;

        if (releaseKitForNeighborhood && session is not null)
        {
            ParityProgress.Phase("Releasing parity connection so Neighborhood can connect to the kit");
            session.ReleaseKitConnections();
            PrintNeighborhoodSecurityHints(adminPasswordHint ?? "test");
        }

        ParityProgress.Phase($"{label}: paused — press Enter in this window to continue the test");
        try
        {
            Console.ReadLine();
        }
        catch
        {
            // No interactive console available; continue without blocking.
        }

        if (releaseKitForNeighborhood && session is not null)
        {
            ParityProgress.Phase("Reconnecting parity to locked kit (PC user auth — close Neighborhood first)");
            session.ReconnectManagedForSecurity();
        }
    }

    private static void PrintNeighborhoodSecurityHints(string adminPassword)
    {
        var pc = Environment.MachineName;
        ParityProgress.Phase("Neighborhood: open Properties → Security on this console.");
        ParityProgress.Phase($"  Enter admin password '{adminPassword}' and click Manage… to view users and permissions.");
        ParityProgress.Phase("  Unlock console… appears after Manage succeeds (unlock itself needs no password).");
        ParityProgress.Phase($"  Override password via RXDK_PARITY_SECURITY_PASSWORD (default: test).");
        ParityProgress.Phase("  Close Neighborhood before pressing Enter here (kit allows one XBDM session).");
    }

    internal static bool IsTriangleModule(XbdmModLoadNotification module)
    {
        if ((module.Flags & XbdmDebugConstants.DmnModflagXbe) == 0)
            return false;

        return module.Name.Contains("trianglexdk", StringComparison.OrdinalIgnoreCase) ||
               module.Name.Contains("triangle", StringComparison.OrdinalIgnoreCase);
    }
}
