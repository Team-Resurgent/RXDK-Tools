using Rxdk.KitConfig;
using Rxdk.KitConfig.Models;
using Rxdk.Xbdm.KitServices.Services;
using Xunit;

namespace Rxdk.Xbdm.KitServices.Tests;

public sealed class SetDefaultConsoleServiceTests
{
    [Fact]
    public void FindExistingConsoleName_MatchesWireName_CaseInsensitive()
    {
        var dir = CreateTempConfigDir();
        var config = KitConfigProvider.CreateForTesting(dir);
        config.Consoles.AddConsole("MyXbox");

        var existing = SetDefaultConsoleService.FindExistingConsoleName(config, "myxbox", "192.168.1.10");
        Assert.Equal("MyXbox", existing);
    }

    [Fact]
    public void FindExistingConsoleName_MatchesCachedIp()
    {
        var dir = CreateTempConfigDir();
        var config = KitConfigProvider.CreateForTesting(dir);
        config.Consoles.AddConsole("StaleIpLabel");
        config.Addresses.SetAddress("StaleIpLabel", "192.168.1.188");

        var existing = SetDefaultConsoleService.FindExistingConsoleName(config, "RealWireName", "192.168.1.188");
        Assert.Equal("StaleIpLabel", existing);
    }

    [Fact]
    public void FindExistingConsoleName_ReturnsNullWhenNoMatch()
    {
        var dir = CreateTempConfigDir();
        var config = KitConfigProvider.CreateForTesting(dir);
        config.Consoles.AddConsole("Other");

        var existing = SetDefaultConsoleService.FindExistingConsoleName(config, "NewKit", "10.0.0.5");
        Assert.Null(existing);
    }

    [Fact]
    public void KitConfig_AddConsole_DoesNotDuplicateExistingName()
    {
        var dir = CreateTempConfigDir();
        new Rxdk.KitConfig.Stores.JsonConsoleStore(dir).Save(new ConsoleRegistryData());
        var config = KitConfigProvider.CreateForTesting(dir);
        config.Consoles.AddConsole("Alpha");
        config.Consoles.AddConsole("alpha");

        Assert.Single(config.Consoles.GetConsoleNames());
        Assert.Equal("Alpha", config.Consoles.GetConsoleNames()[0]);
    }

    private static string CreateTempConfigDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xbset-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
