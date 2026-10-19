using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbFile;

public enum XbDirSort
{
    None,
    Name,
    Date,
    Size,
}

public sealed class XbDirOptions
{
    public XbRecurseOptions Recurse { get; } = new();
    public bool Bare { get; set; }
    public bool Wide { get; set; }
    public XbDirSort Sort { get; set; } = XbDirSort.None;
    public bool ReverseSort { get; set; }
}

public sealed class XbDirEntryView
{
    public required XbPath Path { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public ulong Size { get; init; }
    public DateTime ChangeTimeUtc { get; init; }
}

public sealed class XbDirService
{
    private readonly XbDirOptions _options;
    private readonly XbConsoleSession? _session;
    private bool _listed;
    private int _fileCount;
    private int _dirCount;
    private ulong _totalBytes;

    public XbDirService(XbDirOptions options, XbConsoleSession? session)
    {
        _options = options;
        _session = session;
    }

    public bool Execute(IReadOnlyList<XbPath> paths)
    {
        if (!_options.Bare && _session is not null)
            PrintBanner(_session.Connection);

        if (paths.Count == 0)
            ListPath(XbPath.Parse("*"));
        else
        {
            foreach (var path in paths)
            {
                // A directory lists its contents; anything else is a pattern (or a single name)
                // matched within its own directory.
                var listPath = path;
                if (!path.HasWildcard && PathExistsAsDirectory(path))
                {
                    listPath = path.Child("*");
                    _listed = true; // an empty directory is a successful (empty) listing
                }
                ListPath(listPath);
            }
        }

        if (!_listed)
        {
            Console.Error.WriteLine(paths.Count == 0
                ? "File Not Found"
                : $"File Not Found: {string.Join(", ", paths.Select(p => p.Original))}");
            return false;
        }

        if (!_options.Bare)
            Console.WriteLine($"{_fileCount,16} File(s) {FormatNumber(_totalBytes),13} bytes");

        return true;
    }

    private void PrintBanner(XbdmConnection conn)
    {
        var name = conn.GetNameOfXbox(resolvable: false);
        var addr = conn.TryResolveXboxAddress();
        if (addr.HasValue)
        {
            var bytes = addr.Value;
            Console.WriteLine(
                $" Xbox target system {name} ({bytes >> 24 & 0xFF}.{bytes >> 16 & 0xFF}.{bytes >> 8 & 0xFF}.{bytes & 0xFF})");
        }
        else
            Console.WriteLine($" Xbox target system {name}");
    }

    private void ListPath(XbPath path)
    {
        var entries = CollectEntries(path);
        if (_options.Sort != XbDirSort.None)
            entries = SortEntries(entries);

        if (_options.Wide)
            PrintWide(entries, path);
        else
        {
            if (!_options.Bare)
                Console.WriteLine($"\n Directory of {path.DisplayPath}\n");

            foreach (var entry in entries)
                PrintEntry(entry);
        }
    }

    private List<XbDirEntryView> CollectEntries(XbPath path)
    {
        var results = new List<XbDirEntryView>();
        CollectEntriesRecursive(path, results);
        return results;
    }

    // `path` is a listing path: its directory, and its name as the pattern to match there.
    private void CollectEntriesRecursive(XbPath path, List<XbDirEntryView> results)
    {
        var pattern = path.Name is "." or "" ? "*" : path.Name;
        foreach (var entry in ListEntries(path))
        {
            if (!XbWildcard.IsMatch(pattern, entry.Name))
                continue;
            results.Add(ReadEntry(path.WithName(entry.Name), entry.Name));
            _listed = true;
        }

        // Subdirectories are searched with the same pattern whether or not their own names
        // match it (xbdir /s xe:\*.xbe finds xe:\isle\default.xbe).
        if (_options.Recurse.Recursive || _options.Recurse.SearchSubdirs)
        {
            foreach (var entry in ListEntries(path))
            {
                if (entry.IsDirectory)
                    CollectEntriesRecursive(path.WithName(entry.Name).Child(pattern), results);
            }
        }
    }

    private XbDirEntryView ReadEntry(XbPath path, string name)
    {
        if (path.IsXbox)
        {
            var entry = XbXboxFs.GetAttributes(_session!.Connection, path);
            var isDir = XbXboxFs.IsDirectory(entry);
            if (isDir)
                _dirCount++;
            else
            {
                _fileCount++;
                _totalBytes += entry.Size;
            }

            return new XbDirEntryView
            {
                Path = path,
                Name = name,
                IsDirectory = isDir,
                Size = entry.Size,
                ChangeTimeUtc = DateTimeOffset.FromUnixTimeSeconds(entry.ChangeTimeUnix).UtcDateTime,
            };
        }

        var attrs = XbLocalFs.GetAttributes(path);
        var isDirectory = attrs.HasFlag(FileAttributes.Directory);
        if (isDirectory)
            _dirCount++;
        else
        {
            _fileCount++;
            _totalBytes += (ulong)XbLocalFs.GetSize(path);
        }

        return new XbDirEntryView
        {
            Path = path,
            Name = name,
            IsDirectory = isDirectory,
            Size = isDirectory ? 0 : (ulong)XbLocalFs.GetSize(path),
            ChangeTimeUtc = XbLocalFs.GetChangeTime(path),
        };
    }

    private void PrintEntry(XbDirEntryView entry)
    {
        if (_options.Bare)
        {
            Console.WriteLine(_options.Recurse.SearchSubdirs || _options.Recurse.Recursive
                ? entry.Path.DisplayPath
                : entry.Name);
            return;
        }

        var local = entry.ChangeTimeUtc.ToLocalTime();
        var time = $"{local.Month,2:D2}/{local.Day,2:D2}/{local.Year,4:D4}  " +
                   $"{local.Hour,2:D2}:{local.Minute,2:D2}";
        var size = entry.IsDirectory ? "    <DIR>" : entry.Size.ToString().PadLeft(18);
        Console.WriteLine($"{time,-18} {size,18} {entry.Name}");
    }

    private void PrintWide(List<XbDirEntryView> entries, XbPath path)
    {
        const int columns = 5;
        var names = entries.Select(e => e.Name).ToList();
        var col = 0;
        foreach (var name in names)
        {
            Console.Write($"{name,-20}");
            col++;
            if (col >= columns)
            {
                Console.WriteLine();
                col = 0;
            }
        }

        if (col > 0)
            Console.WriteLine();
    }

    private List<XbDirEntryView> SortEntries(List<XbDirEntryView> entries)
    {
        IEnumerable<XbDirEntryView> sorted = entries;
        switch (_options.Sort)
        {
            case XbDirSort.Name:
                sorted = entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
                break;
            case XbDirSort.Size:
                sorted = entries.OrderBy(e => e.Size);
                break;
            case XbDirSort.Date:
                sorted = entries.OrderBy(e => e.ChangeTimeUtc);
                break;
        }

        if (_options.ReverseSort)
            sorted = sorted.Reverse();

        return sorted.ToList();
    }

    /// <summary>The entries of a listing path's directory (hidden ones only with /h).</summary>
    private IEnumerable<(string Name, bool IsDirectory)> ListEntries(XbPath listing)
    {
        if (listing.IsXbox)
        {
            foreach (var entry in XbXboxFs.ListDirectory(_session!.Connection, listing))
            {
                if (!_options.Recurse.IncludeHidden && (entry.Attributes & XbdmConstants.AttrHidden) != 0)
                    continue;
                yield return (entry.Name, XbXboxFs.IsDirectory(entry));
            }
        }
        else
        {
            var dir = listing.WithName("*");
            foreach (var name in XbLocalFs.EnumerateNames(dir, _options.Recurse.IncludeHidden))
                yield return (name, Directory.Exists(dir.WithName(name).LocalFullPath));
        }
    }

    private bool PathExistsAsDirectory(XbPath path)
    {
        if (path.IsXbox)
            return XbXboxFs.TryGetAttributes(_session!.Connection, path, out var e) && e is not null &&
                   XbXboxFs.IsDirectory(e);

        return Directory.Exists(path.LocalFullPath);
    }

    private static string FormatNumber(ulong value)
    {
        return value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
