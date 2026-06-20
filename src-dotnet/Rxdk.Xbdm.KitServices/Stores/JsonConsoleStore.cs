using System.Text.Json;
using Microsoft.Win32;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.KitServices.Stores;

public class JsonConsoleStore : IConsoleStore
{
    private const string LegacyRegistryPath = @"Software\Microsoft\XboxSDK\RXDKNeighborhood\Consoles";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configPath;

    public JsonConsoleStore(string? configDirectory = null)
    {
        var dir = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RXDKNeighborhood");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "consoles.json");
    }

    public ConsoleRegistryData Load()
    {
        if (!File.Exists(_configPath))
        {
            var migrated = TryMigrateFromRegistry();
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
        try
        {
            var dmDefault = XbdmSession.GetDefaultConsoleName();
            if (!string.IsNullOrWhiteSpace(dmDefault))
                return dmDefault;
        }
        catch
        {
            // fall through to json default
        }

        return Load().DefaultConsole;
    }

    private ConsoleRegistryData TryMigrateFromRegistry()
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
