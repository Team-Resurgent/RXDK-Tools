using System.Diagnostics.CodeAnalysis;
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

    [Fact]
    public void Comprehensive_kit_report()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
        {
            _output.WriteLine("Skipped: set RXDK_TEST_CONSOLE to run kit hardware tests report.");
            return;
        }

        var exitCode = KitTestProgram.Run();
        if (exitCode != 0)
            Assert.Fail(exitCode == 2 ? "Kit test run skipped or could not open session." : "Kit tests failed — see report.");
    }

    [Fact]
    public void Connection_kit_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmKitChecks.RunConnectionChecks(session));
    }

    [Fact]
    public void File_kit_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmKitChecks.RunFileChecks(session));
    }

    [Fact]
    public void Debug_kit_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmKitChecks.RunDebugChecks(session));
    }

    [Fact]
    public void Launch_kit_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmKitChecks.RunLaunchChecks(session));
    }

    [Fact]
    public void Extended_kit_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmKitChecks.RunExtendedChecks(session));
    }

    [Fact]
    public void Execution_kit_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmKitChecks.RunExecutionChecks(session));
    }

    [Fact]
    public void Bridge_kit_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmKitChecks.RunBridgeChecks(session.ConsoleName, session));
    }

    [Fact]
    public void Reboot_kit_checks()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out _))
        {
            _output.WriteLine("Skipped: set RXDK_TEST_CONSOLE.");
            return;
        }

        AssertAllPassed(XbdmKitChecks.RunRebootChecks());
    }

    private bool TryOpenSession([NotNullWhen(true)] out XbdmKitSession? session)
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
        {
            _output.WriteLine("Skipped: set RXDK_TEST_CONSOLE.");
            session = null;
            return false;
        }

        if (!XbdmKitSession.TryCreate(console, out var created, out var reason))
        {
            _output.WriteLine($"Skipped: {reason}");
            session = null;
            return false;
        }

        session = created;
        return true;
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
