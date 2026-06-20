using RXDKNeighborhood.Core.Models;
using Rxdk.Xbdm.KitServices.Stores;

namespace RXDKNeighborhood.Core.Services;

public sealed class ConsoleRegistryService : JsonConsoleStore
{
    public new ConsoleRegistryData Load()
    {
        var data = base.Load();
        var result = new ConsoleRegistryData { DefaultConsole = data.DefaultConsole };
        foreach (var console in data.Consoles)
            result.Consoles.Add(new ConsoleInfo { Name = console.Name, Added = console.Added });
        return result;
    }

    public void Save(ConsoleRegistryData data)
    {
        base.Save(new Rxdk.Xbdm.KitServices.Models.ConsoleRegistryData
        {
            DefaultConsole = data.DefaultConsole,
            Consoles = data.Consoles.Select(c => new Rxdk.Xbdm.KitServices.Models.ConsoleInfo
            {
                Name = c.Name,
                Added = c.Added,
            }).ToList(),
        });
    }
}
