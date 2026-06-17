using Rxdk.Native;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;
using Xunit;

namespace Rxdk.Xbdm.Tests;

[Collection("XboxHardware")]
public sealed class XbdmParityTests
{
    private static string? TestConsole =>
        Environment.GetEnvironmentVariable("RXDK_TEST_CONSOLE");

    private static IXbdmConnection ConnectWithOptionalPassword(IXbdmClient client, string console)
    {
        var password = Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD");
        if (client is ManagedXbdmClient managed && !string.IsNullOrWhiteSpace(password))
            return managed.Connect(console, new XbdmConnectOptions { AdminPassword = password });

        return client.Connect(console);
    }

    [Fact]
    public void ListDrives_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        var nativeDrives = nativeConn.ListDrives();
        var managedDrives = managedConn.ListDrives();

        Assert.Equal(nativeDrives, managedDrives);
    }

    [Fact]
    public void ListDirectory_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        var drive = nativeConn.ListDrives().FirstOrDefault();
        if (drive == default)
            return;

        var path = $"{drive}:\\";
        var nativeEntries = nativeConn.ListDirectory(path).OrderBy(e => e.Name).ToArray();
        var managedEntries = managedConn.ListDirectory(path).OrderBy(e => e.Name).ToArray();

        Assert.Equal(nativeEntries.Length, managedEntries.Length);
        for (var i = 0; i < nativeEntries.Length; i++)
        {
            Assert.Equal(nativeEntries[i].Name, managedEntries[i].Name);
            Assert.Equal(nativeEntries[i].Attributes, managedEntries[i].Attributes);
            Assert.Equal(nativeEntries[i].Size, managedEntries[i].Size);
        }
    }

    [Fact]
    public void GetFileAttributes_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        var drive = nativeConn.ListDrives().FirstOrDefault();
        if (drive == default)
            return;

        var path = $"{drive}:\\";
        var nativeAttr = nativeConn.GetFileAttributes(path);
        var managedAttr = managedConn.GetFileAttributes(path);

        Assert.Equal(nativeAttr.Name, managedAttr.Name);
        Assert.Equal(nativeAttr.Attributes, managedAttr.Attributes);
        Assert.Equal(nativeAttr.Size, managedAttr.Size);
    }

    [Fact]
    public void GetDiskFreeSpace_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        var drive = nativeConn.ListDrives().FirstOrDefault();
        if (drive == default)
            return;

        var driveWire = $"{drive}:\\";
        var nativeSpace = nativeConn.GetDiskFreeSpace(driveWire);
        var managedSpace = managedConn.GetDiskFreeSpace(driveWire);

        Assert.Equal(nativeSpace.FreeBytes, managedSpace.FreeBytes);
        Assert.Equal(nativeSpace.TotalBytes, managedSpace.TotalBytes);
    }

    [Fact]
    public void GetXbeLaunchPath_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        string nativePath;
        try
        {
            nativePath = nativeConn.GetXbeLaunchPath();
        }
        catch (XbdmException)
        {
            return;
        }

        var managedPath = managedConn.GetXbeLaunchPath();
        Assert.Equal(nativePath, managedPath);
    }

    [Fact]
    public void IsSecurityEnabled_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        Assert.Equal(nativeConn.IsSecurityEnabled(), managedConn.IsSecurityEnabled());
    }

    [Fact]
    public void SupportsUserPrivileges_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        Assert.Equal(nativeConn.SupportsUserPrivileges(), managedConn.SupportsUserPrivileges());
    }

    [Fact]
    public void GetUserAccess_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        if (!nativeConn.SupportsUserPrivileges())
            return;

        Assert.Equal(nativeConn.GetUserAccess(), managedConn.GetUserAccess());
    }

    [Fact]
    public void ListUsers_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        if (!nativeConn.SupportsUserPrivileges())
            return;

        var nativeUsers = nativeConn.ListUsers().OrderBy(u => u.UserName).ToArray();
        var managedUsers = managedConn.ListUsers().OrderBy(u => u.UserName).ToArray();

        Assert.Equal(nativeUsers.Length, managedUsers.Length);
        for (var i = 0; i < nativeUsers.Length; i++)
        {
            Assert.Equal(nativeUsers[i].UserName, managedUsers[i].UserName);
            Assert.Equal(nativeUsers[i].AccessPrivileges, managedUsers[i].AccessPrivileges);
        }
    }

    [Fact]
    public void CaptureScreenshot_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        var nativePath = Path.Combine(Path.GetTempPath(), $"xbdm-native-{Guid.NewGuid():N}.bmp");
        var managedPath = Path.Combine(Path.GetTempPath(), $"xbdm-managed-{Guid.NewGuid():N}.bmp");
        try
        {
            nativeConn.CaptureScreenshot(nativePath);
            managedConn.CaptureScreenshot(managedPath);

            var nativeBmp = BmpTestHelper.ReadInfo(nativePath);
            var managedBmp = BmpTestHelper.ReadInfo(managedPath);

            Assert.Equal(nativeBmp.Width, managedBmp.Width);
            Assert.Equal(nativeBmp.Height, managedBmp.Height);
            Assert.Equal(nativeBmp.BitCount, managedBmp.BitCount);
            Assert.Equal(nativeBmp.PixelDataSize, managedBmp.PixelDataSize);
        }
        finally
        {
            if (File.Exists(nativePath))
                File.Delete(nativePath);
            if (File.Exists(managedPath))
                File.Delete(managedPath);
        }
    }

    [Fact]
    public void TryResolveXboxAddress_native_and_managed_match()
    {
        var console = TestConsole;
        if (string.IsNullOrWhiteSpace(console))
            return;

        if (System.Net.IPAddress.TryParse(console, out _))
            return;

        using var native = CreateClient(new NativeXbdmClient());
        using var managed = CreateClient(new ManagedXbdmClient());

        using var nativeConn = ConnectWithOptionalPassword(native, console);
        using var managedConn = ConnectWithOptionalPassword(managed, console);

        Assert.Equal(nativeConn.TryResolveXboxAddress(), managedConn.TryResolveXboxAddress());
    }

    private static IXbdmClient CreateClient(IXbdmClient client)
    {
        client.Initialize();
        return client;
    }
}
