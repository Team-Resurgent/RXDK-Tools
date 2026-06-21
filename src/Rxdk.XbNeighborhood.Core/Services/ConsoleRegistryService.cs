using Rxdk.XbNeighborhood.Core.Models;
using Rxdk.KitConfig;
using Rxdk.KitConfig.Stores;

namespace Rxdk.XbNeighborhood.Core.Services;

public sealed class ConsoleRegistryService : Rxdk.Xbdm.KitServices.Stores.IConsoleStore
{
    private readonly IConsoleStore _store;

    public ConsoleRegistryService()
    {
        _store = KitConfigProvider.CreateDefault().Consoles;
    }

    public ConsoleRegistryService(IConsoleStore store) => _store = store;

    public ConsoleRegistryData Load()
    {
        if (_store is JsonConsoleStore jsonStore)
        {
            var jsonData = jsonStore.Load();
            return MapData(jsonData);
        }

        var names = _store.GetConsoleNames();
        var registryData = new Rxdk.KitConfig.Models.ConsoleRegistryData
        {
            DefaultConsole = _store.GetDefaultConsoleName(),
            Consoles = names.Select(n => new Rxdk.KitConfig.Models.ConsoleInfo
            {
                Name = n,
                Added = DateTimeOffset.UtcNow,
            }).ToList(),
        };
        return MapData(registryData);
    }

    public void Save(ConsoleRegistryData data)
    {
        if (_store is JsonConsoleStore jsonStore)
        {
            jsonStore.Save(new Rxdk.KitConfig.Models.ConsoleRegistryData
            {
                DefaultConsole = data.DefaultConsole,
                Consoles = data.Consoles.Select(c => new Rxdk.KitConfig.Models.ConsoleInfo
                {
                    Name = c.Name,
                    Added = c.Added,
                    IpAddress = c.IpAddress,
                }).ToList(),
            });
            return;
        }

        foreach (var console in data.Consoles)
            _store.AddConsole(console.Name);

        if (!string.IsNullOrWhiteSpace(data.DefaultConsole))
            _store.SetDefaultConsole(data.DefaultConsole);
    }

    public IReadOnlyList<string> GetConsoleNames() => _store.GetConsoleNames();

    public void AddConsole(string name) => _store.AddConsole(name);

    public void RemoveConsole(string name) => _store.RemoveConsole(name);

    public void SetDefaultConsole(string name) => _store.SetDefaultConsole(name);

    public bool IsDefaultConsole(string name) => _store.IsDefaultConsole(name);

    public string? GetDefaultConsoleName() => _store.GetDefaultConsoleName();

    private static ConsoleRegistryData MapData(Rxdk.KitConfig.Models.ConsoleRegistryData data)
    {
        var result = new ConsoleRegistryData { DefaultConsole = data.DefaultConsole };
        foreach (var console in data.Consoles)
            result.Consoles.Add(new ConsoleInfo
            {
                Name = console.Name,
                Added = console.Added,
                IpAddress = console.IpAddress,
            });
        return result;
    }
}
