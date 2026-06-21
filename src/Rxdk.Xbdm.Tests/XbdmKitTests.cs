using Rxdk.Xbdm.Tests.Hardware;
using Xunit;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Individual hardware test facts retained for granular CI reporting.
/// See <see cref="KitTestReportTests"/> for the full markdown report.
/// </summary>
[Collection("XboxHardware")]
public sealed class XbdmKitTests
{
    [Fact]
    public void ListDrives_managed_smoke() => RunSingle("Connection", "ListDrives", XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void ListDirectory_managed_smoke() => RunCategory(XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void GetFileAttributes_managed_smoke() => RunCategory(XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void GetDiskFreeSpace_managed_smoke() => RunCategory(XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void IsSecurityEnabled_managed_smoke() => RunCategory(XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void SupportsUserPrivileges_managed_smoke() => RunCategory(XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void GetUserAccess_managed_smoke() => RunCategory(XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void CaptureScreenshot_managed_smoke() => RunCategory(XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void TryResolveXboxAddress_managed_smoke() => RunCategory(XbdmKitChecks.RunConnectionChecks);

    [Fact]
    public void File_roundtrip_managed_smoke() => RunCategory(XbdmKitChecks.RunFileChecks);

    [Fact]
    public void Debug_thread_list_managed_smoke() => RunCategory(XbdmKitChecks.RunDebugChecks);

    [Fact]
    public void Upload_triangle_xbe_managed_smoke() => RunCategory(XbdmKitChecks.RunLaunchChecks);

    private static void RunSingle(
        string category,
        string name,
        Func<XbdmKitSession, IReadOnlyList<KitCheckResult>> runner)
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        if (!XbdmKitSession.TryCreate(console, out var session, out _))
            return;

        using (session)
        {
            var result = runner(session).FirstOrDefault(r => r.Category == category && r.Name == name);
            if (result is null)
                return;
            if (result.Status == KitCheckStatus.Failed)
                Assert.Fail(result.ToString());
        }
    }

    private static void RunCategory(Func<XbdmKitSession, IReadOnlyList<KitCheckResult>> runner)
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        if (!XbdmKitSession.TryCreate(console, out var session, out _))
            return;

        using (session)
        {
            var failures = runner(session).Where(r => r.Status == KitCheckStatus.Failed).ToArray();
            if (failures.Length > 0)
                Assert.Fail(string.Join(Environment.NewLine, failures.Select(f => f.ToString())));
        }
    }
}
