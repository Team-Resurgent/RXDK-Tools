using Xunit;

namespace Rxdk.XbFile.Tests;

/// <summary>
/// xbcp's tree logic, on local paths (the same code walks Xbox paths). xbcp -r used to copy a
/// tree's files into the destination's parent, flattening it.
/// </summary>
public sealed class XbCopyServiceTests : IDisposable
{
    private readonly TempTree _t = new TempTree()
        .File("src/LEGO/top.txt")
        .File("src/LEGO/Scripts/isle.si")
        .File("src/LEGO/data/world.wdb")
        .File("src/LEGO/data/sub/deep.bin");

    private static readonly string[] Tree =
    {
        "Scripts/isle.si", "data/sub/deep.bin", "data/world.wdb", "top.txt",
    };

    public void Dispose() => _t.Dispose();

    private bool Copy(Action<XbCopyOptions> configure, string dest, params string[] sources)
    {
        var options = new XbCopyOptions { Quiet = true, ForceReplace = true };
        configure(options);
        return new XbCopyService(options, session: null)
            .Execute(sources.Select(s => XbPath.Parse(_t.P(s))).ToList(), XbPath.Parse(_t.P(dest)));
    }

    [Fact]
    public void Recursive_ToANewDestination_CreatesItWithTheWholeTree()
    {
        Assert.True(Copy(o => { o.Recurse.Recursive = true; o.EnsureDestDir = true; }, "out/LEGO", "src/LEGO"));
        Assert.Equal(Tree, _t.Files("out/LEGO"));
        Assert.Equal(Tree.Select(f => "LEGO/" + f), _t.Files("out")); // nothing beside it
    }

    [Fact]
    public void Recursive_IntoAnExistingDirectory_NestsTheTreeUnderItsName()
    {
        _t.Dir("out");
        Assert.True(Copy(o => o.Recurse.Recursive = true, "out", "src/LEGO"));
        Assert.Equal(Tree, _t.Files("out/LEGO"));
        Assert.Empty(Directory.GetFiles(_t.P("out")));
    }

    [Fact]
    public void Recursive_IntoAnExistingDirectoryOfTheSameName_NestsAgain()
    {
        // cp/xcopy semantics: copying LEGO into an existing LEGO directory puts it inside.
        _t.Dir("out/LEGO");
        Assert.True(Copy(o => o.Recurse.Recursive = true, "out/LEGO", "src/LEGO"));
        Assert.Equal(Tree, _t.Files("out/LEGO/LEGO"));
    }

    [Fact]
    public void Wildcard_CopiesTheMatchesIntoTheDestination()
    {
        _t.File("src/LEGO/notes.txt").Dir("out");
        Assert.True(Copy(_ => { }, "out", "src/LEGO/*.txt"));
        Assert.Equal(new[] { "notes.txt", "top.txt" }, _t.Files("out"));
    }

    [Fact]
    public void SearchSubdirs_OnADirectory_CopiesItsContentsIntoTheDestination()
    {
        _t.Dir("out");
        Assert.True(Copy(o => o.Recurse.SearchSubdirs = true, "out", "src/LEGO"));
        Assert.Equal(Tree, _t.Files("out"));
    }

    [Fact]
    public void File_ToAnExistingDirectory_KeepsItsName()
    {
        _t.Dir("out");
        Assert.True(Copy(_ => { }, "out", "src/LEGO/top.txt"));
        Assert.Equal(new[] { "top.txt" }, _t.Files("out"));
    }

    [Fact]
    public void File_ToANewName_IsRenamed()
    {
        _t.Dir("out");
        Assert.True(Copy(_ => { }, "out/renamed.txt", "src/LEGO/top.txt"));
        Assert.Equal(new[] { "renamed.txt" }, _t.Files("out"));
    }

    [Fact]
    public void Directory_WithoutRecursion_IsRefused()
    {
        var ex = Assert.Throws<XbFileException>(() => Copy(_ => { }, "out/LEGO", "src/LEGO"));
        Assert.Contains("Is a directory", ex.Message);
    }
}
