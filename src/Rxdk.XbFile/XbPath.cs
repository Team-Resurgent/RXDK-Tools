namespace Rxdk.XbFile;

/// <summary>
/// Parsed path for PC or Xbox (x-prefixed) file operations — port of legacy <c>FIL</c>.
/// </summary>
public sealed class XbPath
{
    public bool IsXbox { get; }
    public string DirectoryPath { get; }
    public string Name { get; }
    public string Original { get; }

    private XbPath(bool isXbox, string directoryPath, string name, string original)
    {
        IsXbox = isXbox;
        DirectoryPath = directoryPath;
        Name = name;
        Original = original;
    }

    public static XbPath Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new XbFileException("Bad path.");

        var raw = input.Trim();
        if (raw.Length >= 3 && (raw[0] == 'x' || raw[0] == 'X') && raw[2] == ':')
        {
            var drive = raw[1];
            if (!char.IsLetter(drive))
                throw new XbFileException($"Bad Xbox path: {raw}");

            var wire = raw[1..].Replace('/', '\\');
            var (dir, name) = SplitWirePath(wire);
            return new XbPath(true, dir, name, raw);
        }

        var localFull = Path.GetFullPath(raw);
        var localDir = Path.GetDirectoryName(localFull);
        var localName = Path.GetFileName(localFull);
        if (string.IsNullOrEmpty(localDir))
            localDir = ".";
        if (string.IsNullOrEmpty(localName))
            localName = ".";

        return new XbPath(false, localDir, localName, raw);
    }

    public bool HasWildcard => XbWildcard.IsWildcard(Name);

    public string DisplayPath
    {
        get
        {
            if (IsXbox)
            {
                var body = Name is "." or ""
                    ? DirectoryPath.TrimEnd('\\')
                    : $"{DirectoryPath.TrimEnd('\\')}\\{Name}";
                return "x" + body;
            }

            if (Name is "." or "")
                return DirectoryPath;

            return Path.Combine(DirectoryPath, Name);
        }
    }

    public string LocalFullPath
    {
        get
        {
            if (IsXbox)
                throw new InvalidOperationException("Not a local path.");

            if (Name is "." or "")
                return DirectoryPath;

            return Path.Combine(DirectoryPath, Name);
        }
    }

    public string WirePath
    {
        get
        {
            if (!IsXbox)
                throw new InvalidOperationException("Not an Xbox path.");

            if (Name is "." or "")
                return DirectoryPath.EndsWith('\\') ? DirectoryPath : DirectoryPath + "\\";

            return $"{DirectoryPath.TrimEnd('\\')}\\{Name}";
        }
    }

    public string WireDirectoryPath =>
        IsXbox
            ? (DirectoryPath.EndsWith('\\') ? DirectoryPath : DirectoryPath + "\\")
            : throw new InvalidOperationException("Not an Xbox path.");

    public XbPath WithName(string name) =>
        new(IsXbox, DirectoryPath, name, Original);

    public XbPath WithDirectory(string directoryPath) =>
        new(IsXbox, directoryPath, Name, Original);

    public static (string dir, string name) SplitWirePath(string path)
    {
        path = path.Trim().Replace('/', '\\');
        if (path.EndsWith('\\'))
            path = path.TrimEnd('\\');

        var idx = path.LastIndexOf('\\');
        if (idx <= 2 && path.Length > 2 && path[1] == ':')
        {
            if (idx == 2 && path.Length > 3)
                return (path[..3], path[3..]);

            return (path + "\\", ".");
        }

        if (idx < 0)
            return (".", path);

        return (path[..idx], path[(idx + 1)..]);
    }
}
