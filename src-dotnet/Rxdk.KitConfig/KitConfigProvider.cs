using Rxdk.KitConfig.Models;
using Rxdk.KitConfig.Stores;

namespace Rxdk.KitConfig;

public sealed class KitConfigProvider
{
    public IConsoleStore Consoles { get; }
    public IConsoleAddressStore Addresses { get; }

    private KitConfigProvider(IConsoleStore consoles, IConsoleAddressStore addresses)
    {
        Consoles = consoles;
        Addresses = addresses;
    }

    public static KitConfigProvider CreateDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            var registryConsoles = new RegistryConsoleStore();
            var registryAddresses = new RegistryConsoleAddressStore();
            MigrateJsonToRegistryIfNeeded(registryConsoles, registryAddresses);
            return new KitConfigProvider(registryConsoles, registryAddresses);
        }

        var jsonStore = new JsonConsoleStore();
        return new KitConfigProvider(jsonStore, new JsonConsoleAddressStore(jsonStore));
    }

    public static KitConfigProvider CreateForTesting(string configDirectory)
    {
        var jsonStore = new JsonConsoleStore(configDirectory);
        return new KitConfigProvider(jsonStore, new JsonConsoleAddressStore(jsonStore));
    }

    private static void MigrateJsonToRegistryIfNeeded(
        RegistryConsoleStore registryConsoles,
        RegistryConsoleAddressStore registryAddresses)
    {
        if (registryConsoles.GetConsoleNames().Count > 0)
            return;

        var jsonPath = KitConfigPaths.GetConsolesJsonPath();
        if (!File.Exists(jsonPath))
            return;

        try
        {
            var jsonStore = new JsonConsoleStore();
            var data = jsonStore.Load();
            if (data.Consoles.Count == 0)
                return;

            foreach (var console in data.Consoles)
            {
                registryConsoles.AddConsole(console.Name);
                if (!string.IsNullOrWhiteSpace(console.IpAddress))
                    registryAddresses.SetAddress(console.Name, console.IpAddress);
            }

            if (!string.IsNullOrWhiteSpace(data.DefaultConsole))
                registryConsoles.SetDefaultConsole(data.DefaultConsole);
        }
        catch
        {
            // migration is best-effort
        }
    }
}
