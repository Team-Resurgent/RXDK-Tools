using Xunit;

namespace Rxdk.XbFile.Tests;

/// <summary>
/// Child names an entry inside a path; WithName names a sibling. xbdir and xbcp used WithName
/// where they meant Child, so xbdir xe:\isle listed xe:\ and xbcp -r flattened a tree into its
/// parent directory.
/// </summary>
public sealed class XbPathTests
{
    [Fact]
    public void Child_OfAnXboxDirectory_IsInsideIt()
    {
        var child = XbPath.Parse(@"xe:\isle").Child("LEGO");
        Assert.Equal(@"e:\isle\LEGO", child.WirePath);
        Assert.Equal(@"xe:\isle\LEGO", child.DisplayPath);
    }

    [Fact]
    public void ChildPattern_OfAnXboxDirectory_ListsThatDirectory()
    {
        var listing = XbPath.Parse(@"xe:\isle").Child("*");
        Assert.Equal(@"e:\isle\", listing.WireDirectoryPath);
        Assert.Equal("*", listing.Name);
    }

    [Fact]
    public void Child_OfTheXboxDriveRoot_IsAtTheRoot()
    {
        var root = XbPath.Parse(@"xe:\");
        Assert.Equal(@"e:\isle", root.Child("isle").WirePath);
        Assert.Equal(@"e:\", root.Child("*").WireDirectoryPath);
    }

    [Theory]
    [InlineData(@"xe:\")]
    [InlineData(@"xE:\")]
    [InlineData(@"xe:")]
    public void DriveRoot_IsTheRootDirectory(string input)
    {
        var root = XbPath.Parse(input);
        Assert.Equal(".", root.Name);
        Assert.Equal(char.ToLowerInvariant(input[1]), char.ToLowerInvariant(root.WirePath[0]));
        Assert.Equal(@":\", root.WirePath[1..]);
    }

    [Fact]
    public void EntryUnderTheRoot_SplitsAtTheRootSeparator()
    {
        var p = XbPath.Parse(@"xe:\isle");
        Assert.Equal(@"e:\", p.DirectoryPath);
        Assert.Equal("isle", p.Name);
    }

    [Fact]
    public void Child_WithTrailingSeparator_IsInsideTheDirectory()
    {
        Assert.Equal(@"e:\isle\LEGO", XbPath.Parse(@"xe:\isle\").Child("LEGO").WirePath);
    }

    [Fact]
    public void WithName_IsASibling()
    {
        Assert.Equal(@"e:\isle\b.xbe", XbPath.Parse(@"xe:\isle\a.xbe").WithName("b.xbe").WirePath);
    }

    [Fact]
    public void Child_OfALocalDirectory_IsInsideIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rxdk-xbpath");
        Assert.Equal(Path.Combine(dir, "LEGO"), XbPath.Parse(dir).Child("LEGO").LocalFullPath);
    }
}
