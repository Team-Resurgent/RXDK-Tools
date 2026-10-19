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

        // Incremental copy (-d) compares the console file's change-time against the local
        // source. The console stamps that time with ITS clock, which on a devkit is often
        // unset or badly wrong -- making every unchanged file look older and re-copy every
        // time. Set the kit clock to host UTC first (and relearn the skew) so the times are
        // comparable. Best-effort: a kit that refuses setsystime must still deploy.
        if (_options.CopyIfNewer && _session is not null)
        {
            try { _session.Connection.SyncConsoleClock(); }
            catch (XbdmException ex)
            {
                // Surface it: without a settable clock the -d compare is unreliable and
                // everything re-copies. The user needs to know it's the kit clock.
                Console.WriteLine($"  warning: could not set kit clock (setsystime): {ex.Message}");
            }
        }

        var destIsDir = ResolveDestinationIsDirectory(sources, destination);

        // One source to a destination that is not an existing directory: the destination is
        // the copy's own name (xbcp -r LEGO xe:\isle\LEGO creates LEGO with LEGO's tree).
        if (!destIsDir && sources.Count == 1)
            CopyNode(sources[0], destination);
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
        // A pattern copies its matches; -s on a directory copies the directory's contents.
        // Either way each entry lands inside destDir under its own name.
        XbPath? listing = source.HasWildcard ? source
            : _options.Recurse.SearchSubdirs && PathExistsAsDirectory(source) ? source.Child("*")
            : null;
        if (listing is not null)
        {
            foreach (var name in ListNames(listing, listing.Name))
                CopyNode(listing.WithName(name), destDir.Child(name));
        }
        else
            CopyNode(source, destDir.Child(source.Name));
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
            foreach (var name in ListNames(src.Child("*"), "*"))
                CopyNode(src.Child(name), dst.Child(name));

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

        var reported = false;
        if (PathExists(dst))
        {
            if (_options.CopyIfNewer)
            {
                // FATX change-times are 2-second granular vs a sub-second local mtime, so require
                // the source to be MORE than 2s newer before re-sending; otherwise rounding alone
                // forces a needless re-copy. Report the per-file verdict so an incremental deploy
                // shows what it actually did (add -v for the compared timestamps).
                var srcTime = GetChangeTime(src);
                var dstTime = GetChangeTime(dst);
                var sourceNewer = (srcTime - dstTime).TotalSeconds > 2.0;
                ReportAction(sourceNewer ? "copy" : "skip", dst, srcTime, dstTime);
                reported = true;
                if (!sourceNewer)
                {
                    _anySuccess = true;
                    return;
                }
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

        if (!reported)
            ReportAction("copy", dst, null, null);

        TransferFile(src, dst);
        CopyAttributes(src, dst);
        _anySuccess = true;
    }

    /// <summary>
    /// Print one concise per-file action line ("copy"/"skip" + destination), suppressed by -q.
    /// With -v (and both times known, i.e. the -d comparison) it also shows the compared local
    /// and kit change-times, which is how you tell whether the kit clock is causing re-copies.
    /// </summary>
    private void ReportAction(string verb, XbPath dst, DateTime? local, DateTime? kit)
    {
        if (_options.Quiet)
            return;
        if (_options.Verbose && local is not null && kit is not null)
            Console.WriteLine(
                $"  {verb} {dst.DisplayPath}  " +
                $"(local {local:yyyy-MM-dd HH:mm:ss}Z, kit {kit:yyyy-MM-dd HH:mm:ss}Z)");
        else
            Console.WriteLine($"  {verb} {dst.DisplayPath}");
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

    /// <summary>Names in a listing path's directory that match <paramref name="pattern"/>.</summary>
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
            foreach (var name in XbLocalFs.EnumerateNames(directory.WithName("*"), _options.Recurse.IncludeHidden))
            {
                if (!XbWildcard.IsMatch(pattern, name))
                    continue;
                yield return name;
            }
        }
    }

    private bool HasEntries(XbPath directory) => ListNames(directory.Child("*"), "*").Any();

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
