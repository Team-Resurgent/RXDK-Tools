using Rxdk.Xbdm;

namespace Rxdk.XbFile;

public sealed class XbMkdirService
{
    public void Execute(XbPath path, bool ensureParents, XbConsoleSession? session)
    {
        if (ensureParents)
        {
            var parent = ParentPath(path);
            if (parent is not null)
                EnsureDirectory(parent, session);
        }

        try
        {
            CreateDirectory(path, session);
        }
        catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
        {
            if (IsDirectory(path, session))
                Console.Error.WriteLine($"warning: directory already exists: {path.DisplayPath}");
            else
                throw new XbFileException($"Cannot create directory: {path.DisplayPath}", ex);
        }
    }

    private static void CreateDirectory(XbPath path, XbConsoleSession? session)
    {
        if (path.IsXbox)
            XbXboxFs.CreateDirectory(session!.Connection, path);
        else
            XbLocalFs.CreateDirectory(path);
    }

    private static void EnsureDirectory(XbPath path, XbConsoleSession? session)
    {
        if (path.IsXbox)
            XbXboxFs.EnsureDirectoryTree(session!.Connection, path);
        else
            XbLocalFs.EnsureDirectory(path);
    }

    private static bool IsDirectory(XbPath path, XbConsoleSession? session)
    {
        if (path.IsXbox)
            return XbXboxFs.TryGetAttributes(session!.Connection, path, out var e) && e is not null &&
                   XbXboxFs.IsDirectory(e);

        return Directory.Exists(path.LocalFullPath);
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
