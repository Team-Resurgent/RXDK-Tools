using Rxdk.Xbdm.Tests.Parity;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Direct console entry for scripts — avoids VSTest/xUnit double-printing progress lines.
/// </summary>
internal static class ParityDirectProgram
{
    public static int Main(string[] args)
    {
        if (args.Length >= 1 && string.Equals(args[0], "--unlock", StringComparison.OrdinalIgnoreCase))
        {
            var console = args.Length >= 2
                ? args[1]
                : Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(console))
            {
                Console.Error.WriteLine("Usage: --unlock <console>  (or set RXDK_TEST_CONSOLE)");
                return 2;
            }

            var password = args.Length >= 3
                ? args[2]
                : Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")
                    ?? Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD");
            return XbdmKitUnlock.Run(console, password);
        }

        if (args.Length == 0 || !string.Equals(args[0], "--parity", StringComparison.OrdinalIgnoreCase))
            return 0;

        return Run();
    }

    internal static int Run()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
        {
            Console.Error.WriteLine("Set RXDK_TEST_CONSOLE to run hardware parity.");
            return 2;
        }

        var summary = XbdmParityChecks.RunAll(console);
        var reportPath = XbdmParityReport.WriteToDefaultPath(summary);
        var failures = summary.Results.Where(r => r.Status == ParityStatus.Failed).ToArray();

        Console.WriteLine();
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Summary: {summary.FormatSummaryLine()}");

        foreach (var failure in failures)
            Console.Error.WriteLine(failure.ToString());

        if (summary.Results.Count == 1 &&
            summary.Results[0] is { Category: "Session", Name: "Open", Status: ParityStatus.Skipped })
        {
            Console.Error.WriteLine($"Could not run parity checks: {summary.Results[0].Notes}");
            return 2;
        }

        return failures.Length > 0 ? 1 : 0;
    }
}
