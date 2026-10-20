using Rxdk.Xbdm.Tests.Hardware;
using Xunit;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Individual hardware checks, one per test for granular CI reporting: each runs its category's
/// checks and reports the one it names. See <see cref="KitTestReportTests"/> for the full
/// markdown report.
/// </summary>
[Collection("XboxHardware")]
public sealed class XbdmKitTests
{
    [KitFact]
    public void ListDrives_managed_smoke() => RunSingle("ListDrives", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void ListDirectory_managed_smoke() => RunSingle("ListDirectory", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void GetFileAttributes_managed_smoke() => RunSingle("GetFileAttributes", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void GetDiskFreeSpace_managed_smoke() => RunSingle("GetDiskFreeSpace", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void IsSecurityEnabled_managed_smoke() => RunSingle("IsSecurityEnabled", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void SupportsUserPrivileges_managed_smoke() => RunSingle("SupportsUserPrivileges", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void GetUserAccess_managed_smoke() => RunSingle("GetUserAccess", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void CaptureScreenshot_managed_smoke() => RunSingle("CaptureScreenshot", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void TryResolveXboxAddress_managed_smoke() => RunSingle("TryResolveXboxAddress", XbdmKitChecks.RunConnectionChecks);

    [KitFact]
    public void File_roundtrip_managed_smoke() => RunSingle("FileRoundTrip", XbdmKitChecks.RunFileChecks);

    [KitFact]
    public void Debug_thread_list_managed_smoke() => RunSingle("GetThreadList", XbdmKitChecks.RunDebugChecks);

    [KitFact]
    public void Upload_triangle_xbe_managed_smoke() => RunSingle("UploadTriangleXbe", XbdmKitChecks.RunLaunchChecks);

    private static void RunSingle(string name, Func<XbdmKitSession, IReadOnlyList<KitCheckResult>> runner)
    {
        using var session = KitTestReportTests.OpenSession();
        var results = runner(session);
        var result = results.FirstOrDefault(r => r.Name == name);
        if (result is null)
            Assert.Fail($"No check named {name} ran; the checks were: {string.Join(", ", results.Select(r => r.Name))}");
        if (result.Status == KitCheckStatus.Failed)
            Assert.Fail(result.ToString());
    }
}
