using Xunit;

namespace Rxdk.XbFile.Tests;

[CollectionDefinition(nameof(ConsoleCapture), DisableParallelization = true)]
public sealed class ConsoleCapture;

/// <summary>
/// xbdir's listing logic, on local paths (the same code walks Xbox paths). xbdir used to list a
/// directory's parent instead of the directory, list the whole directory for a single file, and
/// build an invalid path for a wildcard.
/// </summary>
[Collection(nameof(ConsoleCapture))]
public sealed class XbDirServiceTests : IDisposable
{
    private readonly TempTree _t = new TempTree()
        .File("root/other.txt")
        .File("root/isle/default.xbe")
        .File("root/isle/readme.txt")
        .File("root/isle/LEGO/Scripts/isle.si")
        .File("root/isle/LEGO/data/extra.xbe");

    public void Dispose() => _t.Dispose();

    private (bool ok, string[] lines) Dir(Action<XbDirOptions> configure, params string[] paths)
    {
        var options = new XbDirOptions { Bare = true };
        configure(options);
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var ok = new XbDirService(options, session: null).Execute(paths.Select(p => XbPath.Parse(_t.P(p))).ToList());
            var lines = outWriter.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            return (ok, lines);
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    private string Rel(string full) => Path.GetRelativePath(_t.Root, full).Replace(Path.DirectorySeparatorChar, '/');

    [Fact]
    public void Directory_ListsItsOwnContents()
    {
        var (ok, lines) = Dir(_ => { }, "root/isle");
        Assert.True(ok);
        Assert.Equal(new[] { "LEGO", "default.xbe", "readme.txt" }, lines.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Directory_WithTrailingSeparator_ListsItsOwnContents()
    {
        var (_, lines) = Dir(_ => { }, "root/isle" + Path.DirectorySeparatorChar);
        Assert.Equal(new[] { "LEGO", "default.xbe", "readme.txt" }, lines.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void File_ListsOnlyThatFile()
    {
        var (ok, lines) = Dir(_ => { }, "root/isle/default.xbe");
        Assert.True(ok);
        Assert.Equal(new[] { "default.xbe" }, lines);
    }

    [Fact]
    public void Wildcard_ListsTheMatchesInItsDirectory()
    {
        var (ok, lines) = Dir(_ => { }, "root/isle/*.xbe");
        Assert.True(ok);
        Assert.Equal(new[] { "default.xbe" }, lines);
    }

    [Fact]
    public void SearchSubdirs_FindsMatchesBelowNonMatchingDirectories()
    {
        var (ok, lines) = Dir(o => o.Recurse.SearchSubdirs = true, "root/*.xbe");
        Assert.True(ok);
        Assert.Equal(new[] { "root/isle/LEGO/data/extra.xbe", "root/isle/default.xbe" },
                     lines.Select(Rel).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Recursive_ListsTheWholeTreeOnce()
    {
        var (_, lines) = Dir(o => o.Recurse.Recursive = true, "root/isle/LEGO");
        Assert.Equal(new[]
        {
            "root/isle/LEGO/Scripts", "root/isle/LEGO/Scripts/isle.si",
            "root/isle/LEGO/data", "root/isle/LEGO/data/extra.xbe",
        }, lines.Select(Rel).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EmptyDirectory_IsASuccessfulEmptyListing()
    {
        _t.Dir("root/empty");
        var (ok, lines) = Dir(_ => { }, "root/empty");
        Assert.True(ok);
        Assert.Empty(lines);
    }

    [Fact]
    public void MissingPath_Fails()
    {
        var (ok, lines) = Dir(_ => { }, "root/missing");
        Assert.False(ok);
        Assert.Empty(lines);
    }
}
