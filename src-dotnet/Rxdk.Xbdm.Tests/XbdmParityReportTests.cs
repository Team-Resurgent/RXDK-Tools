using System.Diagnostics.CodeAnalysis;
using Rxdk.Xbdm.Tests.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Runs native vs managed checks and writes a markdown parity report.
/// Requires <c>RXDK_TEST_CONSOLE</c>; optional <c>RXDK_TEST_PASSWORD</c>.
/// Set <c>RXDK_PARITY_REPORT</c> to override report path (default: artifacts/xbdm-parity-report.md).
/// Set <c>RXDK_PARITY_ALLOW_LAUNCH=1</c> or <c>RXDK_PARITY_ALLOW_EXEC=1</c> for live XBE info checks.
/// Set <c>RXDK_PARITY_ALLOW_EXEC=1</c> for launch/debug execution parity and bridge symbols.
/// Set <c>RXDK_PARITY_ALLOW_REBOOT=1</c> for warm reboot parity (runs last).
/// Set <c>RXDK_PARITY_ALLOW_SECURITY=1</c> for temporary user add/remove tests.
/// </summary>
[Collection("XboxHardware")]
public sealed class XbdmParityReportTests
{
    private readonly ITestOutputHelper _output;

    public XbdmParityReportTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Comprehensive_parity_report()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
        {
            _output.WriteLine("Skipped: set RXDK_TEST_CONSOLE to run hardware parity report.");
            return;
        }

        var exitCode = ParityDirectProgram.Run();
        if (exitCode != 0)
            Assert.Fail(exitCode == 2 ? "Parity run skipped or could not open session." : "Parity checks failed — see report.");
    }

    [Fact]
    public void Connection_parity_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmParityChecks.RunConnectionChecks(session));
    }

    [Fact]
    public void File_parity_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmParityChecks.RunFileChecks(session));
    }

    [Fact]
    public void Debug_parity_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmParityChecks.RunDebugChecks(session));
    }

    [Fact]
    public void Launch_parity_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmParityChecks.RunLaunchChecks(session));
    }

    [Fact]
    public void Extended_parity_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmParityChecks.RunExtendedChecks(session));
    }

    [Fact]
    public void Execution_parity_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmParityChecks.RunExecutionChecks(session));
    }

    [Fact]
    public void Bridge_parity_checks()
    {
        if (!TryOpenSession(out var session))
            return;

        using (session)
            AssertAllPassed(XbdmParityChecks.RunBridgeChecks(session.ConsoleName, session));
    }

    [Fact]
    public void Reboot_parity_checks()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out _))
        {
            _output.WriteLine("Skipped: set RXDK_TEST_CONSOLE.");
            return;
        }

        AssertAllPassed(XbdmParityChecks.RunRebootChecks());
    }

    private bool TryOpenSession([NotNullWhen(true)] out XbdmParitySession? session)
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
        {
            _output.WriteLine("Skipped: set RXDK_TEST_CONSOLE.");
            session = null;
            return false;
        }

        if (!XbdmParitySession.TryCreate(console, out var created, out var reason))
        {
            _output.WriteLine($"Skipped: {reason}");
            session = null;
            return false;
        }

        session = created;
        return true;
    }

    private void AssertAllPassed(IReadOnlyList<ParityCheckResult> results)
    {
        foreach (var result in results)
            _output.WriteLine(result.ToString());

        var failures = results.Where(r => r.Status == ParityStatus.Failed).ToArray();
        if (failures.Length > 0)
            Assert.Fail(string.Join(Environment.NewLine, failures.Select(f => f.ToString())));
    }
}
