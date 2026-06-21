using Microsoft.Win32;

namespace Rxdk.KitConfig.Stores;

/// <summary>
/// Maps console wire names to last-known IP addresses for connect when UDP name broadcast fails.
/// HKCU\Software\Microsoft\XboxSDK\xbshlext\Addresses
/// </summary>
public class RegistryConsoleAddressStore : IConsoleAddressStore
{
    public const string RegistryPath = @"Software\Microsoft\XboxSDK\xbshlext\Addresses";

    public void SetAddress(string consoleName, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(consoleName))
            throw new ArgumentException("Console name is required.", nameof(consoleName));
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new ArgumentException("IP address is required.", nameof(ipAddress));

        using var key = OpenKey(writable: true);
        if (key == null)
            throw new InvalidOperationException("Could not open shell extension address registry key.");

        key.SetValue(consoleName.Trim(), ipAddress.Trim(), RegistryValueKind.String);
    }

    public string? TryGetAddress(string consoleName)
    {
        if (string.IsNullOrWhiteSpace(consoleName) || !OperatingSystem.IsWindows())
            return null;

        using var key = OpenKey(writable: false);
        return key?.GetValue(consoleName.Trim()) as string;
    }

    public void RemoveAddress(string consoleName)
    {
        if (string.IsNullOrWhiteSpace(consoleName) || !OperatingSystem.IsWindows())
            return;

        using var key = OpenKey(writable: true);
        key?.DeleteValue(consoleName.Trim(), throwOnMissingValue: false);
    }

    private static RegistryKey? OpenKey(bool writable)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        return Registry.CurrentUser.CreateSubKey(
            RegistryPath,
            writable ? RegistryKeyPermissionCheck.ReadWriteSubTree : RegistryKeyPermissionCheck.ReadSubTree);
    }
}
