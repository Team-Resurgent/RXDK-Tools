using System.Runtime.Versioning;
using Microsoft.Win32;
using Rxdk.Xbdm.Managed;

namespace Rxdk.KitConfig.Stores;

/// <summary>
/// Legacy shell extension console list at HKCU\Software\Microsoft\XboxSDK\xbshlext\Consoles.
/// Default value is REG_DWORD count; each console name is a value name with REG_DWORD payload.
/// </summary>
[SupportedOSPlatform("windows")]
public class RegistryConsoleStore : IConsoleStore
{
    public const string RegistryPath = @"Software\Microsoft\XboxSDK\xbshlext\Consoles";
    private const string DefaultConsoleRegistryPath = @"Software\Microsoft\XboxSDK";
    private const string DefaultConsoleValueName = "XboxName";

    public IReadOnlyList<string> GetConsoleNames()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<string>();

        using var key = OpenKey(writable: false);
        if (key == null)
            return Array.Empty<string>();

        var names = new List<string>();
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name))
                continue;
            names.Add(name);
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void AddConsole(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Console name is required.", nameof(name));

        using var key = OpenKey(writable: true);
        if (key == null)
            throw new InvalidOperationException("Could not open shell extension console registry key.");

        if (key.GetValue(name) != null)
            return;

        key.SetValue(name, 0, RegistryValueKind.DWord);
        var count = ReadCount(key) + 1;
        key.SetValue(string.Empty, count, RegistryValueKind.DWord);
    }

    public void RemoveConsole(string name)
    {
        using var key = OpenKey(writable: true);
        if (key == null)
            return;

        if (key.GetValue(name) == null)
            return;

        key.DeleteValue(name, throwOnMissingValue: false);
        var count = Math.Max(0, ReadCount(key) - 1);
        key.SetValue(string.Empty, count, RegistryValueKind.DWord);
    }

    public void SetDefaultConsole(string name)
    {
        AddConsole(name);
        XbdmSession.SetDefaultConsoleName(name);
    }

    public void ClearDefaultConsole()
    {
        XbdmSession.ClearDefaultConsoleName();
    }

    public bool IsDefaultConsole(string name)
    {
        var defaultName = GetConfiguredDefaultConsoleName();
        return !string.IsNullOrWhiteSpace(defaultName) &&
               string.Equals(defaultName, name, StringComparison.OrdinalIgnoreCase);
    }

    public string? GetDefaultConsoleName()
    {
        var names = GetConsoleNames();
        var configured = GetConfiguredDefaultConsoleName();

        if (!string.IsNullOrWhiteSpace(configured) &&
            names.Any(n => string.Equals(n, configured, StringComparison.OrdinalIgnoreCase)))
            return configured;

        if (names.Count > 0)
            return names[0];

        try
        {
            XbdmSession.EnsureInitialized();
            var dmDefault = XbdmSession.GetDefaultConsoleName();
            if (!string.IsNullOrWhiteSpace(dmDefault))
                return dmDefault;
        }
        catch
        {
        }

        return configured;
    }

    public static string? GetConfiguredDefaultConsoleName()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        using var key = Registry.CurrentUser.OpenSubKey(DefaultConsoleRegistryPath);
        var value = key?.GetValue(DefaultConsoleValueName) as string;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static RegistryKey? OpenKey(bool writable)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        return Registry.CurrentUser.CreateSubKey(
            RegistryPath,
            writable ? RegistryKeyPermissionCheck.ReadWriteSubTree : RegistryKeyPermissionCheck.ReadSubTree);
    }

    private static int ReadCount(RegistryKey key)
    {
        var value = key.GetValue(string.Empty);
        return value switch
        {
            int i => i,
            uint u => (int)u,
            _ => 0,
        };
    }
}
