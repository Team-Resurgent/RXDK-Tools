namespace Rxdk.Bundler;

/// <summary>
/// XDK .rdf files spell Windows paths (backslash, optional drive). On Unix a
/// backslash is a legal filename character, so Path.Combine/GetFullPath will not
/// collapse <c>..\..\Common\Media\foo.tga</c> unless separators are rewritten first.
/// </summary>
internal static class BundlerPath
{
    public static string ToNative(string path)
    {
        if (string.IsNullOrEmpty(path) || Path.DirectorySeparatorChar == '\\')
            return path;
        return path.Replace('\\', '/');
    }

    public static bool IsWindowsDrive(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    /// <summary>Resolve an RDF-relative path against the .rdf directory (PathPrefix).</summary>
    public static string ResolveAgainst(string basePath, string relative)
    {
        relative = ToNative(relative);
        if (IsWindowsDrive(relative))
            return relative;
        basePath = ToNative(basePath ?? "");
        if (Path.IsPathRooted(relative))
            return Path.GetFullPath(relative);
        var root = string.IsNullOrEmpty(basePath) ? "." : basePath;
        return Path.GetFullPath(Path.Combine(root, relative));
    }
}
