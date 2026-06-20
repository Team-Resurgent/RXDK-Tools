using Rxdk.Xbdm.Managed;
using Rxdk.Xbdm.Tests.Hardware;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Direct console entry for scripts — avoids VSTest/xUnit double-printing progress lines.
/// </summary>
internal static class KitTestProgram
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

        if (args.Length >= 1 && string.Equals(args[0], "--debugname", StringComparison.OrdinalIgnoreCase))
        {
            var console = args.Length >= 2
                ? args[1]
                : Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(console))
            {
                Console.Error.WriteLine("Usage: --debugname <console-or-ip>  (or set RXDK_TEST_CONSOLE)");
                return 2;
            }

            try
            {
                XbdmSession.EnsureInitialized();
                using var connection = XbdmSession.Connect(console);
                Console.WriteLine($"DEBUGNAME: {connection.GetNameOfXbox(resolvable: false)}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        if (args.Length == 0 ||
            (!string.Equals(args[0], "--kit", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(args[0], "--parity", StringComparison.OrdinalIgnoreCase)))
            return 0;

        return Run();
    }

    internal static int Run()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
        {
            Console.Error.WriteLine("Set RXDK_TEST_CONSOLE to run kit hardware tests.");
            return 2;
        }

        var summary = XbdmKitChecks.RunAll(console);
        var reportPath = KitTestReport.WriteToDefaultPath(summary);
        var failures = summary.Results.Where(r => r.Status == KitCheckStatus.Failed).ToArray();

        Console.WriteLine();
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Summary: {summary.FormatSummaryLine()}");

        foreach (var failure in failures)
            Console.Error.WriteLine(failure.ToString());

        if (summary.Results.Count == 1 &&
            summary.Results[0] is { Category: "Session", Name: "Open", Status: KitCheckStatus.Skipped })
        {
            Console.Error.WriteLine($"Could not run kit tests: {summary.Results[0].Notes}");
            return 2;
        }

        return failures.Length > 0 ? 1 : 0;
    }
}
