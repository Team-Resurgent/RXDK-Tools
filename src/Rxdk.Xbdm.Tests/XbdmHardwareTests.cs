using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;
using Xunit;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Optional live-console tests. Set <c>RXDK_TEST_CONSOLE</c> (name or IP, e.g. 192.168.1.184).
/// For secured kits set <c>RXDK_TEST_PASSWORD</c> to the admin password.
/// </summary>
[Collection("XboxHardware")]
public sealed class XbdmHardwareTests
{
    [KitFact]
    public void Managed_connect_and_list_drives()
    {
        var console = XbdmTestEnvironment.Console;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);

        var drives = conn.ListDrives();
        Assert.NotEmpty(drives);
    }

    [KitFact]
    public void Managed_list_root_directory()
    {
        var console = XbdmTestEnvironment.Console;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);

        var drive = conn.ListDrives().First();
        var entries = conn.ListDirectory($"{drive}:\\");
        Assert.NotNull(entries);
    }

    [KitFact]
    public void Managed_reports_security_state()
    {
        var console = XbdmTestEnvironment.Console;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);

        _ = conn.IsSecurityEnabled();
        _ = conn.SupportsUserPrivileges();
    }

    [KitFact("RXDK_TEST_PASSWORD")]
    public void Managed_secure_admin_connect_when_password_configured()
    {
        var console = XbdmTestEnvironment.Console;
        var password = Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD");

        using var client = CreateManagedClient();
        using var conn = client.Connect(console, new XbdmConnectOptions { AdminPassword = password });
        Assert.NotEmpty(conn.ListDrives());
    }

    [KitFact]
    public void Managed_disk_free_space_returns_nonzero()
    {
        var console = XbdmTestEnvironment.Console;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);
        var drive = conn.ListDrives().First();
        var space = conn.GetDiskFreeSpace($"{drive}:\\");
        Assert.True(space.TotalBytes > 0, "Expected non-zero total disk space.");
    }

    [KitFact]
    public void Managed_capture_screenshot_writes_valid_bmp()
    {
        var console = XbdmTestEnvironment.Console;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);

        var path = Path.Combine(Path.GetTempPath(), $"xbdm-hw-{Guid.NewGuid():N}.bmp");
        try
        {
            conn.CaptureScreenshot(path);
            Assert.True(File.Exists(path));
            var bmp = BmpTestHelper.ReadInfo(path);
            Assert.True(bmp.Width > 0);
            Assert.True(bmp.Height > 0);
            Assert.Equal((ushort)24, bmp.BitCount);
            Assert.Equal(bmp.Width * bmp.Height * 3, bmp.PixelDataSize);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static ManagedXbdmClient CreateManagedClient()
    {
        var client = new ManagedXbdmClient();
        client.Initialize();
        return client;
    }

    private static ManagedXbdmConnection Connect(ManagedXbdmClient client, string console)
    {
        var password = Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD");
        if (!string.IsNullOrWhiteSpace(password))
            return (ManagedXbdmConnection)client.Connect(console, new XbdmConnectOptions { AdminPassword = password });

        return (ManagedXbdmConnection)client.Connect(console);
    }
}

internal static class XbdmTestEnvironment
{
    /// <summary>The kit a [KitFact] runs against (it only runs when this is set).</summary>
    public static string Console =>
        TryGetConsole(out var console) ? console : throw new InvalidOperationException("RXDK_TEST_CONSOLE is not set.");

    public static bool TryGetConsole(out string console)
    {
        console = Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(console);
    }
}
