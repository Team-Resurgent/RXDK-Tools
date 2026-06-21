using Rxdk.KitConfig.Models;
using Rxdk.KitConfig.Stores;

namespace Rxdk.KitConfig.Tests;

public class JsonConsoleStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonConsoleStore _store;

    public JsonConsoleStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rxdk-kitconfig-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new JsonConsoleStore(_tempDir);
        _store.Save(new ConsoleRegistryData());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void AddConsole_PersistsNames()
    {
        _store.AddConsole("Alpha");
        _store.AddConsole("Beta");

        var names = _store.GetConsoleNames();
        Assert.Equal(2, names.Count);
        Assert.Contains("Alpha", names);
        Assert.Contains("Beta", names);
    }

    [Fact]
    public void RemoveConsole_DropsEntry()
    {
        _store.AddConsole("Alpha");
        _store.AddConsole("Beta");
        _store.SetDefaultConsole("Beta");
        _store.RemoveConsole("Alpha");

        Assert.Single(_store.GetConsoleNames());
        Assert.Equal("Beta", _store.GetDefaultConsoleName());
    }

    [Fact]
    public void GetDefaultConsoleName_FallsBackWhenConfiguredDefaultNotInList()
    {
        _store.Save(new ConsoleRegistryData
        {
            DefaultConsole = "StaleName",
            Consoles = new List<ConsoleInfo> { new() { Name = "myxbox" } },
        });

        Assert.Equal("myxbox", _store.GetDefaultConsoleName());
    }

    [Fact]
    public void JsonAddressStore_PersistsIpOnConsole()
    {
        var addresses = new JsonConsoleAddressStore(_store);
        _store.AddConsole("Kit");
        addresses.SetAddress("Kit", "192.168.1.10");

        Assert.Equal("192.168.1.10", addresses.TryGetAddress("Kit"));
    }
}
