using Rxdk.KitConfig.Models;

namespace Rxdk.KitConfig.Stores;

public sealed class JsonConsoleAddressStore : IConsoleAddressStore
{
    private readonly JsonConsoleStore _consoleStore;

    public JsonConsoleAddressStore(JsonConsoleStore? consoleStore = null)
    {
        _consoleStore = consoleStore ?? new JsonConsoleStore();
    }

    public void SetAddress(string consoleName, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(consoleName))
            throw new ArgumentException("Console name is required.", nameof(consoleName));
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new ArgumentException("IP address is required.", nameof(ipAddress));

        var data = _consoleStore.Load();
        var trimmedName = consoleName.Trim();
        var entry = data.Consoles.FirstOrDefault(c =>
            string.Equals(c.Name, trimmedName, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            data.Consoles.Add(new ConsoleInfo
            {
                Name = trimmedName,
                Added = DateTimeOffset.UtcNow,
                IpAddress = ipAddress.Trim(),
            });
        }
        else
        {
            var index = data.Consoles.IndexOf(entry);
            data.Consoles[index] = new ConsoleInfo
            {
                Name = entry.Name,
                Added = entry.Added,
                IpAddress = ipAddress.Trim(),
            };
        }

        _consoleStore.Save(data);
    }

    public string? TryGetAddress(string consoleName)
    {
        if (string.IsNullOrWhiteSpace(consoleName))
            return null;

        var data = _consoleStore.Load();
        var entry = data.Consoles.FirstOrDefault(c =>
            string.Equals(c.Name, consoleName.Trim(), StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(entry?.IpAddress) ? null : entry.IpAddress;
    }

    public void RemoveAddress(string consoleName)
    {
        if (string.IsNullOrWhiteSpace(consoleName))
            return;

        var data = _consoleStore.Load();
        var entry = data.Consoles.FirstOrDefault(c =>
            string.Equals(c.Name, consoleName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return;

        var index = data.Consoles.IndexOf(entry);
        data.Consoles[index] = new ConsoleInfo
        {
            Name = entry.Name,
            Added = entry.Added,
            IpAddress = null,
        };
        _consoleStore.Save(data);
    }
}
