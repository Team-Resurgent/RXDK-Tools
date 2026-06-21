namespace Rxdk.KitConfig;

public static class KitConfigPaths
{
    public const string AppFolderName = "Rxdk.XbNeighborhood";
    public const string LegacyAppFolderName = "RXDKNeighborhood";
    public const string ConsolesFileName = "consoles.json";

    public static string GetConfigDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, AppFolderName);
        var legacyDir = Path.Combine(appData, LegacyAppFolderName);
        if (!Directory.Exists(configDir) && Directory.Exists(legacyDir))
            return legacyDir;

        return configDir;
    }

    public static string GetConsolesJsonPath()
    {
        return Path.Combine(GetConfigDirectory(), ConsolesFileName);
    }
}
