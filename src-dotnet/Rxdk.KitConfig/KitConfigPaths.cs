namespace Rxdk.KitConfig;

public static class KitConfigPaths
{
    public const string AppFolderName = "RXDKNeighborhood";
    public const string ConsolesFileName = "consoles.json";

    public static string GetConfigDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
    }

    public static string GetConsolesJsonPath()
    {
        return Path.Combine(GetConfigDirectory(), ConsolesFileName);
    }
}
