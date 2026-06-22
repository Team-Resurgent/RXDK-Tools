namespace Rxdk.XbFile;

public sealed class XbeCopyService
{
    public void Execute(string localSource, string remoteDestination, bool noLogo, XbConsoleSession session)
    {
        if (!noLogo)
        {
            var version = typeof(XbeCopyService).Assembly.GetName().Version?.ToString() ?? "1.0";
            Console.Error.WriteLine($"Microsoft (R) Xbox image remote copy {version}");
            Console.Error.WriteLine("Copyright (C) Microsoft Corporation.  All rights reserved.\n");
        }

        if (!File.Exists(localSource))
            throw new XbFileException($"Cannot access file: {localSource}");

        var remote = NormalizeRemotePath(remoteDestination);
        var parent = ParentWirePath(remote);
        if (parent is not null)
            XbXboxFs.EnsureDirectoryTree(session.Connection, XbPath.Parse("x" + parent));

        XbXboxFs.SendFile(session.Connection, localSource, remote);

        var attrs = File.GetAttributes(localSource);
        XbXboxFs.SetAttributesFromLocal(session.Connection, XbPath.Parse("x" + remote), attrs);
    }

    private static string NormalizeRemotePath(string path)
    {
        path = path.Trim().Replace('/', '\\');
        if (path.Length > 0 && (path[0] == 'x' || path[0] == 'X') && path.Length > 2 && path[2] == ':')
            path = path[1..];

        if (path.Length < 2 || path[1] != ':')
            throw new XbFileException($"Bad Xbox path: {path}");

        return path;
    }

    private static string? ParentWirePath(string wirePath)
    {
        var trimmed = wirePath.TrimEnd('\\');
        var idx = trimmed.LastIndexOf('\\');
        if (idx <= 2)
            return null;
        return trimmed[..idx];
    }
}
