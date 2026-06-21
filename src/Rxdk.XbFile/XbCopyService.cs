using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbFile;

public sealed class XbCopyService
{
    private readonly XbCopyOptions _options;
    private readonly XbConsoleSession? _session;
    private bool _anySuccess;
    private bool _replaceAll;

    public XbCopyService(XbCopyOptions options, XbConsoleSession? session)
    {
        _options = options;
        _session = session;
    }

    public bool Execute(IReadOnlyList<XbPath> sources, XbPath destination)
    {
        if (destination.HasWildcard)
            throw new XbFileException("Wildcards not allowed in destination name.");

        var destIsDir = ResolveDestinationIsDirectory(sources, destination);

        if (!destIsDir && sources.Count == 1)
            CopyFileOrDirectory(sources[0], destination);
        else
        {
            foreach (var src in sources)
                CopySourceToDirectory(src, destination);
        }

        return _anySuccess;
    }

    private bool ResolveDestinationIsDirectory(IReadOnlyList<XbPath> sources, XbPath destination)
    {
        var needsDir = sources.Count > 1 || sources.Any(s => s.HasWildcard) || _options.Recurse.SearchSubdirs;
        if (!needsDir)
            return PathExistsAsDirectory(destination);

        if (_options.EnsureDestDir)
            EnsureDirectory(destination);

        if (!PathExistsAsDirectory(destination))
            throw new XbFileException($"Destination is not a directory: {destination.DisplayPath}");

        return true;
    }

    private void CopySourceToDirectory(XbPath source, XbPath destDir)
    {
        if (source.HasWildcard || _options.Recurse.SearchSubdirs)
        {
            var pattern = source.HasWildcard ? source.Name : "*";
            var listingDir = source.HasWildcard ? source : source.WithName("*");
            foreach (var name in ListNames(listingDir, pattern))
            {
                var srcChild = ChildPath(source, name);
                var dstChild = destDir.WithName(name);
                CopyNode(srcChild, dstChild);
            }
        }
        else
            CopyNode(source, destDir.WithName(source.Name));
    }

    private void CopyNode(XbPath src, XbPath dst)
    {
        if (PathExistsAsDirectory(src))
        {
            if (!_options.Recurse.Recursive && !_options.Recurse.SearchSubdirs)
                throw new XbFileException($"Is a directory: {src.DisplayPath}");

            if (_options.SkipEmptyDirs && !HasEntries(src))
                return;

            EnsureDirectory(dst);
            foreach (var name in ListNames(src.WithName("*"), "*"))
            {
                var childSrc = ChildPath(src, name);
                var childDst = dst.WithName(name);
                CopyNode(childSrc, childDst);
            }

            _anySuccess = true;
            return;
        }

        CopyFileOrDirectory(src, dst);
    }

    private void CopyFileOrDirectory(XbPath src, XbPath dst)
    {
        if (PathExistsAsDirectory(src))
        {
            if (PathExists(dst) && !PathExistsAsDirectory(dst))
                throw new XbFileException($"Destination is a directory: {dst.DisplayPath}");

            if (_options.CopyIfNewer && PathExists(dst))
            {
                _anySuccess = true;
                return;
            }

            EnsureDirectory(dst);
            _anySuccess = true;
            return;
        }

        if (PathExists(dst))
        {
            if (_options.CopyIfNewer && GetChangeTime(dst) >= GetChangeTime(src))
            {
                _anySuccess = true;
                return;
            }

            if (IsReadOnly(dst) && !_options.ForceReadOnly)
                throw new XbFileException($"Read-only destination: {dst.DisplayPath}");

            if (IsReadOnly(dst) && _options.ForceReadOnly)
                ClearReadOnly(dst);

            if (!ConfirmReplace(dst))
                return;
        }

        if (_options.EnsureDestDir && !PathExistsAsDirectory(dst))
        {
            var parent = ParentPath(dst);
            if (parent is not null)
                EnsureDirectory(parent);
        }

        if (!_options.Quiet)
            Console.WriteLine($"{src.DisplayPath} => {dst.DisplayPath}");

        TransferFile(src, dst);
        CopyAttributes(src, dst);
        _anySuccess = true;
    }

    private void TransferFile(XbPath src, XbPath dst)
    {
        if (!src.IsXbox && !dst.IsXbox)
            XbLocalFs.CopyFile(src, dst, overwrite: true);
        else if (!src.IsXbox && dst.IsXbox)
            XbXboxFs.SendFile(_session!.Connection, src.LocalFullPath, dst.WirePath);
        else if (src.IsXbox && !dst.IsXbox)
            XbXboxFs.ReceiveFile(_session!.Connection, src.WirePath, dst.LocalFullPath);
        else
            XbXboxFs.CopyWireToWire(_session!.Connection, src.WirePath, dst.WirePath, isDirectory: false);
    }

    private void CopyAttributes(XbPath src, XbPath dst)
    {
        if (PathExistsAsDirectory(src))
            return;

        if (!src.IsXbox && !dst.IsXbox)
            XbLocalFs.CopyAttributes(src, dst);
        else if (!src.IsXbox && dst.IsXbox)
            XbXboxFs.SetAttributesFromLocal(_session!.Connection, dst, XbLocalFs.GetAttributes(src));
        else if (src.IsXbox && !dst.IsXbox)
        {
            var entry = XbXboxFs.GetAttributes(_session!.Connection, src);
            var attrs = File.GetAttributes(dst.LocalFullPath);
            if ((entry.Attributes & XbdmConstants.AttrReadOnly) != 0)
                attrs |= FileAttributes.ReadOnly;
            if ((entry.Attributes & XbdmConstants.AttrHidden) != 0)
                attrs |= FileAttributes.Hidden;
            File.SetAttributes(dst.LocalFullPath, attrs);
        }
        else
        {
            var entry = XbXboxFs.GetAttributes(_session!.Connection, src);
            _session.Connection.SetFileAttributes(dst.WirePath, entry.Attributes);
        }
    }

    private static XbPath ChildPath(XbPath parent, string childName)
    {
        if (parent.IsXbox)
        {
            var wire = parent.WireDirectoryPath.TrimEnd('\\');
            if (parent.Name is not "." and not "")
                wire = $"{wire.TrimEnd('\\')}\\{parent.Name}";
            return XbPath.Parse("x" + wire + "\\" + childName);
        }

        var localDir = parent.Name is "." or ""
            ? parent.DirectoryPath
            : Path.Combine(parent.DirectoryPath, parent.Name);
        return XbPath.Parse(Path.Combine(localDir, childName));
    }

    private IEnumerable<string> ListNames(XbPath directory, string pattern)
    {
        if (directory.IsXbox)
        {
            foreach (var entry in XbXboxFs.ListDirectory(_session!.Connection, directory))
            {
                if (!_options.Recurse.IncludeHidden && (entry.Attributes & XbdmConstants.AttrHidden) != 0)
                    continue;
                if (!XbWildcard.IsMatch(pattern, entry.Name))
                    continue;
                yield return entry.Name;
            }
        }
        else
        {
            foreach (var name in XbLocalFs.EnumerateNames(directory.WithName(pattern), _options.Recurse.IncludeHidden))
            {
                if (!XbWildcard.IsMatch(pattern, name))
                    continue;
                yield return name;
            }
        }
    }

    private bool HasEntries(XbPath directory) => ListNames(directory.WithName("*"), "*").Any();

    private bool ConfirmReplace(XbPath dst)
    {
        if (_options.ForceReplace || _replaceAll)
            return true;

        Console.Write($"Overwrite {dst.DisplayPath}? (Yes/No/All): ");
        var line = Console.ReadLine();
        if (line is null)
        {
            Console.WriteLine();
            return false;
        }

        if (line.Length == 0)
            return false;

        switch (line[0])
        {
            case 'y' or 'Y':
                return true;
            case 'a' or 'A':
                _replaceAll = true;
                return true;
            default:
                return false;
        }
    }

    private bool PathExists(XbPath path)
    {
        if (path.IsXbox)
            return XbXboxFs.TryGetAttributes(_session!.Connection, path, out _);

        return File.Exists(path.LocalFullPath) || Directory.Exists(path.LocalFullPath);
    }

    private bool PathExistsAsDirectory(XbPath path)
    {
        if (path.IsXbox)
            return XbXboxFs.TryGetAttributes(_session!.Connection, path, out var e) && e is not null &&
                   XbXboxFs.IsDirectory(e);

        return Directory.Exists(path.LocalFullPath);
    }

    private DateTime GetChangeTime(XbPath path)
    {
        if (path.IsXbox)
        {
            var entry = XbXboxFs.GetAttributes(_session!.Connection, path);
            return DateTimeOffset.FromUnixTimeSeconds(entry.ChangeTimeUnix).UtcDateTime;
        }

        return XbLocalFs.GetChangeTime(path);
    }

    private bool IsReadOnly(XbPath path)
    {
        if (path.IsXbox)
        {
            var entry = XbXboxFs.GetAttributes(_session!.Connection, path);
            return (entry.Attributes & XbdmConstants.AttrReadOnly) != 0;
        }

        return XbLocalFs.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
    }

    private void ClearReadOnly(XbPath path)
    {
        if (path.IsXbox)
        {
            var entry = XbXboxFs.GetAttributes(_session!.Connection, path);
            _session.Connection.SetFileAttributes(path.WirePath, entry.Attributes & ~XbdmConstants.AttrReadOnly);
        }
        else
        {
            var full = path.LocalFullPath;
            File.SetAttributes(full, File.GetAttributes(full) & ~FileAttributes.ReadOnly);
        }
    }

    private void EnsureDirectory(XbPath path)
    {
        if (path.IsXbox)
            XbXboxFs.EnsureDirectoryTree(_session!.Connection, path);
        else
            XbLocalFs.EnsureDirectory(path);
    }

    private static XbPath? ParentPath(XbPath path)
    {
        if (path.IsXbox)
        {
            var wire = path.WirePath.TrimEnd('\\');
            var idx = wire.LastIndexOf('\\');
            if (idx <= 2)
                return null;
            return XbPath.Parse("x" + wire[..idx]);
        }

        var parent = Path.GetDirectoryName(path.LocalFullPath);
        return parent is null ? null : XbPath.Parse(parent);
    }
}
