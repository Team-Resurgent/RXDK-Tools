using System.Text;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Parity;

internal static class XbdmParityReport
{
    public static string Format(ParityReportSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# XBDM Native vs Managed Parity Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {summary.GeneratedAt:O}");
        builder.AppendLine($"Console: `{summary.ConsoleName}`");
        builder.AppendLine($"Password configured: {(summary.PasswordConfigured ? "yes" : "no")}");
        builder.AppendLine();
        builder.AppendLine($"**Summary:** {summary.FormatSummaryLine()}");
        builder.AppendLine();
        AppendBackendLegend(builder);
        builder.AppendLine();
        AppendRunbook(builder);
        builder.AppendLine();

        foreach (var group in summary.Results.GroupBy(r => r.Category).OrderBy(g => g.Key))
        {
            builder.AppendLine($"## {group.Key}");
            builder.AppendLine();
            builder.AppendLine($"_Backend: {ParityBackend.Describe(group.Key)}_");
            builder.AppendLine();
            builder.AppendLine("| Check | Status | Native | Managed | Notes |");
            builder.AppendLine("|-------|--------|--------|---------|-------|");
            foreach (var result in group)
            {
                builder.Append('|').Append(Escape(result.Name)).Append('|')
                    .Append(result.DisplayStatus).Append('|')
                    .Append(Escape(result.NativeDetail ?? "-")).Append('|')
                    .Append(Escape(result.ManagedDetail ?? "-")).Append('|')
                    .Append(Escape(result.Notes ?? "-")).AppendLine("|");
            }

            builder.AppendLine();
        }

        if (summary.Failed > 0)
        {
            builder.AppendLine("## Failures");
            builder.AppendLine();
            foreach (var failure in summary.Results.Where(r => r.Status == ParityStatus.Failed))
                builder.AppendLine($"- `{failure.Category}/{failure.Name}`: {failure.Notes}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string WriteToDefaultPath(ParityReportSummary summary)
    {
        var path = Environment.GetEnvironmentVariable("RXDK_PARITY_REPORT");
        if (string.IsNullOrWhiteSpace(path))
        {
            var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts"));
            Directory.CreateDirectory(artifacts);
            path = Path.Combine(artifacts, "xbdm-parity-report.md");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, Format(summary), Encoding.UTF8);
        return path;
    }

    private static void AppendBackendLegend(StringBuilder builder)
    {
        builder.AppendLine("## Backends");
        builder.AppendLine();
        builder.AppendLine("Each suite exercises a different implementation pairing:");
        builder.AppendLine();
        builder.AppendLine($"- **Connection / File / Reboot** — {ParityBackend.NativeVsManaged}. These hit the real `xbdm.dll` via P/Invoke on the native side.");
        builder.AppendLine($"- **Debug / Extended / Execution** — {ParityBackend.ManagedVsManaged}. `session.NativeDebug` is `NativeXbdmDebugProxy`, which routes debugger commands through the managed stack, so both sides are managed.");
        builder.AppendLine($"- **Launch** — {ParityBackend.Mixed}. File upload uses `xbdm.dll`; `GetXbeInfo` uses the managed debug proxy.");
        builder.AppendLine($"- **Bridge** — {ParityBackend.ManagedBridge}. Drives the managed `xboxdbg-bridge` process over stdio.");
    }

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static void AppendRunbook(StringBuilder builder)
    {
        builder.AppendLine("## Runbook");
        builder.AppendLine();
        builder.AppendLine("Required:");
        builder.AppendLine("- `RXDK_TEST_CONSOLE` — kit name or IP");
        builder.AppendLine("- `RXDK_TEST_PASSWORD` — if the kit uses XBDM security");
        builder.AppendLine();
        builder.AppendLine("Optional (enable skipped suites):");
        builder.AppendLine("- `RXDK_PARITY_ALLOW_EXEC=1` — launches TriangleXDK, debug execution parity, bridge symbols (~2 min). Kit should be at dashboard.");
        builder.AppendLine("- `RXDK_PARITY_ALLOW_LAUNCH=1` — live `GetXbeInfo` without full exec suite");
        builder.AppendLine("- `RXDK_PARITY_ALLOW_BRIDGE=1` — managed `xboxdbg-bridge` smoke tests");
        builder.AppendLine("- `RXDK_PARITY_ALLOW_REBOOT=1` — warm reboot + reconnect (run last; ~2 min)");
        builder.AppendLine("- `RXDK_PARITY_ALLOW_SECURITY=1` — temporary user add/remove on kits with user privileges");
        builder.AppendLine("- `RXDK_PARITY_NO_RESTORE=1` — skip dashboard reboot after exec suite");
        builder.AppendLine("- `RXDK_PARITY_LAUNCH_TIMEOUT_SEC` — max seconds to wait for TriangleXDK load (default 90)");
        builder.AppendLine("- `RXDK_PARITY_TITLE_DWELL_SEC` — seconds to leave TriangleXDK running after launch (default 5)");
        builder.AppendLine("- `RXDK_PARITY_OP_TIMEOUT_SEC` — per XBDM command timeout in seconds (default 30)");
        builder.AppendLine("- `RXDK_BRIDGE_EXE` — path to `xboxdbg-bridge.exe` if not in default build output");
        builder.AppendLine();
        builder.AppendLine("No manual interaction is required when all optional flags are set and the kit is idle at the dashboard.");
    }
}
