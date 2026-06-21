using System.Text.Json;
using Microsoft.Win32;
using Rxdk.KitConfig.Models;
using Rxdk.Xbdm.Managed;

namespace Rxdk.KitConfig.Stores;

public class JsonConsoleStore : IConsoleStore
{
    private const string LegacyRegistryPath = @"Software\Microsoft\XboxSDK\RXDKNeighborhood\Consoles";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configPath;

    public JsonConsoleStore(string? configDirectory = null)
    {
        var dir = configDirectory ?? KitConfigPaths.GetConfigDirectory();
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, KitConfigPaths.ConsolesFileName);
    }

    public ConsoleRegistryData Load()
    {
        if (!File.Exists(_configPath))
        {
            var migrated = TryMigrateFromLegacyRegistry();
            if (migrated.Consoles.Count > 0)
            {
                Save(migrated);
                return migrated;
            }

            return new ConsoleRegistryData();
        }

        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<ConsoleRegistryData>(json, JsonOptions) ?? new ConsoleRegistryData();
    }

    public void Save(ConsoleRegistryData data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public IReadOnlyList<string> GetConsoleNames()
    {
        var data = Load();
        return data.Consoles.Select(c => c.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void AddConsole(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Console name is required.", nameof(name));

        var data = Load();
        if (data.Consoles.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        data.Consoles.Add(new ConsoleInfo { Name = name, Added = DateTimeOffset.UtcNow });
        if (string.IsNullOrWhiteSpace(data.DefaultConsole))
            data.DefaultConsole = name;

        Save(data);
    }

    public void RemoveConsole(string name)
    {
        var data = Load();
        data.Consoles.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(data.DefaultConsole, name, StringComparison.OrdinalIgnoreCase))
            data.DefaultConsole = data.Consoles.FirstOrDefault()?.Name;
        Save(data);
    }

    public void SetDefaultConsole(string name)
    {
        var data = Load();
        if (!data.Consoles.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            data.Consoles.Add(new ConsoleInfo { Name = name, Added = DateTimeOffset.UtcNow });

        data.DefaultConsole = name;
        Save(data);

        if (OperatingSystem.IsWindows())
            XbdmSession.SetDefaultConsoleName(name);
    }

    public bool IsDefaultConsole(string name)
    {
        var defaultName = GetDefaultConsoleName();
        return !string.IsNullOrWhiteSpace(defaultName) &&
               string.Equals(defaultName, name, StringComparison.OrdinalIgnoreCase);
    }

    public string? GetDefaultConsoleName()
    {
        var data = Load();
        var names = data.Consoles.Select(c => c.Name).ToArray();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                XbdmSession.EnsureInitialized();
                var dmDefault = XbdmSession.GetDefaultConsoleName();
                if (!string.IsNullOrWhiteSpace(dmDefault) &&
                    names.Any(n => string.Equals(n, dmDefault, StringComparison.OrdinalIgnoreCase)))
                    return dmDefault;
            }
            catch
            {
                // fall through to json default
            }
        }

        if (!string.IsNullOrWhiteSpace(data.DefaultConsole) &&
            names.Any(n => string.Equals(n, data.DefaultConsole, StringComparison.OrdinalIgnoreCase)))
            return data.DefaultConsole;

        if (names.Length > 0)
            return names[0];

        if (OperatingSystem.IsWindows())
        {
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
        }

        return data.DefaultConsole;
    }

    internal ConsoleRegistryData TryMigrateFromLegacyRegistry()
    {
        var data = new ConsoleRegistryData();
        if (!OperatingSystem.IsWindows())
            return data;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRegistryPath);
            if (key == null)
                return data;

            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                data.Consoles.Add(new ConsoleInfo { Name = name, Added = DateTimeOffset.UtcNow });
            }

            data.DefaultConsole = data.Consoles.FirstOrDefault()?.Name;
            try
            {
                XbdmSession.EnsureInitialized();
                var dmDefault = XbdmSession.GetDefaultConsoleName();
                if (!string.IsNullOrWhiteSpace(dmDefault))
                    data.DefaultConsole = dmDefault;
            }
            catch
            {
                // keep first console as default
            }
        }
        catch
        {
            // ignore registry errors
        }

        return data;
    }
}
