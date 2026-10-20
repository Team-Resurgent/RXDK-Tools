using Rxdk.Xbdm.Tests.Hardware;
using Xunit;
using Xunit.Abstractions;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Runs managed hardware checks and writes a markdown kit test report.
/// Requires <c>RXDK_TEST_CONSOLE</c>; optional <c>RXDK_TEST_PASSWORD</c>.
/// Set <c>RXDK_KIT_REPORT</c> to override report path (default: out/reports/xbdm-kit-report.md).
/// Set <c>RXDK_KIT_ALLOW_LAUNCH=1</c> or <c>RXDK_KIT_ALLOW_EXEC=1</c> for live XBE info checks.
/// Set <c>RXDK_KIT_ALLOW_EXEC=1</c> for launch/debug execution and bridge symbol tests.
/// Set <c>RXDK_KIT_ALLOW_REBOOT=1</c> for warm reboot tests (runs last).
/// Set <c>RXDK_KIT_ALLOW_SECURITY=1</c> for temporary user add/remove tests.
/// </summary>
[Collection("XboxHardware")]
public sealed class KitTestReportTests
{
    private readonly ITestOutputHelper _output;

    public KitTestReportTests(ITestOutputHelper output) => _output = output;

    [KitFact]
    public void Comprehensive_kit_report()
    {
        var exitCode = KitTestProgram.Run();
        if (exitCode != 0)
            Assert.Fail(exitCode == 2 ? "Could not open a session with the kit." : "Kit tests failed — see report.");
    }

    [KitFact]
    public void Connection_kit_checks()
    {
        using (var session = OpenSession())
            AssertAllPassed(XbdmKitChecks.RunConnectionChecks(session));
    }

    [KitFact]
    public void File_kit_checks()
    {
        using (var session = OpenSession())
            AssertAllPassed(XbdmKitChecks.RunFileChecks(session));
    }

    [KitFact]
    public void Debug_kit_checks()
    {
        using (var session = OpenSession())
            AssertAllPassed(XbdmKitChecks.RunDebugChecks(session));
    }

    [KitFact]
    public void Launch_kit_checks()
    {
        using (var session = OpenSession())
            AssertAllPassed(XbdmKitChecks.RunLaunchChecks(session));
    }

    [KitFact]
    public void Extended_kit_checks()
    {
        using (var session = OpenSession())
            AssertAllPassed(XbdmKitChecks.RunExtendedChecks(session));
    }

    [KitFact]
    public void Execution_kit_checks()
    {
        using (var session = OpenSession())
            AssertAllPassed(XbdmKitChecks.RunExecutionChecks(session));
    }

    [KitFact]
    public void Bridge_kit_checks()
    {
        using (var session = OpenSession())
            AssertAllPassed(XbdmKitChecks.RunBridgeChecks(session.ConsoleName, session));
    }

    [KitFact]
    public void Reboot_kit_checks()
    {
        AssertAllPassed(XbdmKitChecks.RunRebootChecks());
    }

    /// <summary>A session with the kit; failing to open one fails the test (the kit was asked for).</summary>
    internal static XbdmKitSession OpenSession()
    {
        var console = XbdmTestEnvironment.Console;
        if (!XbdmKitSession.TryCreate(console, out var session, out var reason))
            Assert.Fail($"Could not open a session with {console}: {reason}");
        return session;
    }

    private void AssertAllPassed(IReadOnlyList<KitCheckResult> results)
    {
        foreach (var result in results)
            _output.WriteLine(result.ToString());

        var failures = results.Where(r => r.Status == KitCheckStatus.Failed).ToArray();
        if (failures.Length > 0)
            Assert.Fail(string.Join(Environment.NewLine, failures.Select(f => f.ToString())));
    }
}
