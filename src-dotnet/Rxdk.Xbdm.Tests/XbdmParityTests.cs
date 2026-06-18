using Rxdk.Xbdm.Tests.Parity;
using Xunit;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Individual parity facts retained for granular CI reporting.
/// See <see cref="XbdmParityReportTests"/> for the full markdown report.
/// </summary>
[Collection("XboxHardware")]
public sealed class XbdmParityTests
{
    [Fact]
    public void ListDrives_native_and_managed_match() => RunSingle("Connection", "ListDrives", XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void ListDirectory_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void GetFileAttributes_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void GetDiskFreeSpace_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void GetXbeLaunchPath_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void IsSecurityEnabled_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void SupportsUserPrivileges_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void GetUserAccess_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void ListUsers_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void CaptureScreenshot_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void TryResolveXboxAddress_native_and_managed_match() => RunCategory(XbdmParityChecks.RunConnectionChecks);

    [Fact]
    public void File_roundtrip_native_and_managed_match() => RunCategory(XbdmParityChecks.RunFileChecks);

    [Fact]
    public void Debug_thread_list_native_and_managed_match() => RunCategory(XbdmParityChecks.RunDebugChecks);

    [Fact]
    public void Upload_triangle_xbe_native_and_managed_match() => RunCategory(XbdmParityChecks.RunLaunchChecks);

    private static void RunSingle(
        string category,
        string name,
        Func<XbdmParitySession, IReadOnlyList<ParityCheckResult>> runner)
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        if (!XbdmParitySession.TryCreate(console, out var session, out _))
            return;

        using (session)
        {
            var result = runner(session).FirstOrDefault(r => r.Category == category && r.Name == name);
            if (result is null)
                return;
            if (result.Status == ParityStatus.Failed)
                Assert.Fail(result.ToString());
        }
    }

    private static void RunCategory(Func<XbdmParitySession, IReadOnlyList<ParityCheckResult>> runner)
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        if (!XbdmParitySession.TryCreate(console, out var session, out _))
            return;

        using (session)
        {
            var failures = runner(session).Where(r => r.Status == ParityStatus.Failed).ToArray();
            if (failures.Length > 0)
                Assert.Fail(string.Join(Environment.NewLine, failures.Select(f => f.ToString())));
        }
    }
}
