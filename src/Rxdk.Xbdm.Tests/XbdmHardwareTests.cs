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
    [Fact]
    public void Managed_connect_and_list_drives()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);

        var drives = conn.ListDrives();
        Assert.NotEmpty(drives);
    }

    [Fact]
    public void Managed_list_root_directory()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);

        var drive = conn.ListDrives().First();
        var entries = conn.ListDirectory($"{drive}:\\");
        Assert.NotNull(entries);
    }

    [Fact]
    public void Managed_reports_security_state()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);

        _ = conn.IsSecurityEnabled();
        _ = conn.SupportsUserPrivileges();
    }

    [Fact]
    public void Managed_secure_admin_connect_when_password_configured()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        var password = Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            return;

        using var client = CreateManagedClient();
        using var conn = client.Connect(console, new XbdmConnectOptions { AdminPassword = password });
        Assert.NotEmpty(conn.ListDrives());
    }

    [Fact]
    public void Managed_disk_free_space_returns_nonzero()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

        using var client = CreateManagedClient();
        using var conn = Connect(client, console);
        var drive = conn.ListDrives().First();
        var space = conn.GetDiskFreeSpace($"{drive}:\\");
        Assert.True(space.TotalBytes > 0, "Expected non-zero total disk space.");
    }

    [Fact]
    public void Managed_capture_screenshot_writes_valid_bmp()
    {
        if (!XbdmTestEnvironment.TryGetConsole(out var console))
            return;

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
    public static bool TryGetConsole(out string console)
    {
        console = Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(console);
    }
}
