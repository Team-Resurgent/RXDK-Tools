namespace Rxdk.KitConfig.Stores;

public interface IConsoleStore
{
    IReadOnlyList<string> GetConsoleNames();
    void AddConsole(string name);
    void RemoveConsole(string name);
    void SetDefaultConsole(string name);
    bool IsDefaultConsole(string name);
    string? GetDefaultConsoleName();
}
