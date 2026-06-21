namespace Rxdk.XbFile;

public sealed class XbRecurseOptions
{
    public bool Recursive { get; set; }
    public bool SearchSubdirs { get; set; }
    public bool IncludeHidden { get; set; }
}

public sealed class XbCopyOptions
{
    public bool Quiet { get; set; }
    public bool ForceReplace { get; set; }
    public bool ForceReadOnly { get; set; }
    public bool CopyIfNewer { get; set; }
    public bool SkipEmptyDirs { get; set; }
    public bool EnsureDestDir { get; set; }
    public XbRecurseOptions Recurse { get; } = new();
}
