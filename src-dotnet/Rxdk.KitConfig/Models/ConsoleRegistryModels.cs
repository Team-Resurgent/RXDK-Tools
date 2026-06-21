namespace Rxdk.KitConfig.Models;

public class ConsoleInfo
{
    public required string Name { get; init; }
    public DateTimeOffset Added { get; init; }
    public string? IpAddress { get; init; }
}

public class ConsoleRegistryData
{
    public string? DefaultConsole { get; set; }
    public List<ConsoleInfo> Consoles { get; set; } = new();
}
