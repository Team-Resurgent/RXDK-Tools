namespace Rxdk.XbFile.Tests;

/// <summary>A scratch directory tree, deleted on dispose.</summary>
internal sealed class TempTree : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("rxdk-xbfile-").FullName;

    public string P(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public TempTree File(string relative, string text = "x")
    {
        var full = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, text);
        return this;
    }

    public TempTree Dir(string relative)
    {
        Directory.CreateDirectory(P(relative));
        return this;
    }

    /// <summary>Every file under <paramref name="relative"/>, as sorted '/'-separated relative paths.</summary>
    public string[] Files(string relative) =>
        Directory.EnumerateFiles(P(relative), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(P(relative), f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            System.IO.File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }
}
