using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.Xbdm.KitServices.Tests;

public class WirePathServiceTests
{
    [Theory]
    [InlineData("MyKit\\D", "D:\\")]
    [InlineData("MyKit\\C:", "C:\\")]
    [InlineData("MyKit\\E\\title\\default.xbe", "E:\\title\\default.xbe")]
    public void TryBuildWirePath_MapsDisplayToWire(string display, string expected)
    {
        Assert.True(WirePathService.TryBuildWirePath(display, out var wire));
        Assert.Equal(expected, wire);
    }

    [Fact]
    public void GetConsoleNameFromDisplayPath_ReturnsPrefix()
    {
        Assert.Equal("KitA", WirePathService.GetConsoleNameFromDisplayPath("KitA\\C\\foo"));
    }

    [Fact]
    public void AppendDisplaySegment_AddsBackslashWhenNeeded()
    {
        Assert.Equal("Kit\\D\\save", WirePathService.AppendDisplaySegment("Kit\\D", "save"));
    }
}
