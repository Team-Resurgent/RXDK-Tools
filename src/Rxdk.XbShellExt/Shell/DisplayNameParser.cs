using Rxdk.XbShellExt.Interop;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.KitServices.Stores;

namespace Rxdk.XbShellExt.Shell;

internal static class DisplayNameParser
{
    private const uint SfgaoValidate = 0x01000000;

    internal readonly record struct ParseResult(
        nint RelativePidl,
        uint CharsEaten,
        bool TrailingSlash,
        IReadOnlyList<string> Segments);

    internal static bool TryParseRelative(
        string parentDisplayPath,
        string displayName,
        out ParseResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        var trimmed = displayName.Trim();
        var normalized = NormalizeInput(trimmed);
        if (normalized.Length == 0)
            return false;

        var (path, _, trailingSlash) = SplitRelativePath(normalized);
        if (path.Length == 0 && !trailingSlash)
            return false;

        var segments = SplitSegments(path);
        if (segments.Count == 0)
            return false;

        NormalizeSegments(parentDisplayPath, segments);

        nint pidl = 0;
        try
        {
            pidl = BuildRelativePidl(segments);
            if (pidl == 0)
                return false;

            result = new ParseResult(pidl, (uint)trimmed.Length, trailingSlash, segments);
            return true;
        }
        catch
        {
            if (pidl != 0)
                PidlHelper.Free(pidl);
            return false;
        }
    }

    internal static bool TryResolveAttributes(
        string parentDisplayPath,
        IReadOnlyList<string> segments,
        bool trailingSlash,
        bool validate,
        ref uint pdwAttributes)
    {
        if (!TryResolveTarget(parentDisplayPath, segments, validate, out var item))
            return false;

        if (trailingSlash && !item.IsDirectory)
            return false;

        pdwAttributes &= item.Attributes;
        return true;
    }

    internal static bool WantsValidation(uint pdwAttributes) =>
        (pdwAttributes & SfgaoValidate) != 0;

    private static string NormalizeInput(string displayName)
    {
        var text = displayName.Trim();
        if (text.StartsWith("xbox://", StringComparison.OrdinalIgnoreCase))
        {
            text = text[7..];
            text = text.Replace('/', '\\');
            text = text.TrimEnd('\\');
        }

        const string prefix = ShellConstants.NamespaceDisplayName + "\\";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            text = text[prefix.Length..];

        return text.Trim().Replace('/', '\\');
    }

    private static (string Path, uint Eaten, bool TrailingSlash) SplitRelativePath(string text)
    {
        var start = 0;
        while (start < text.Length && text[start] == '\\')
            start++;

        var end = text.Length;
        var trailingSlash = end > start && text[end - 1] == '\\';
        if (trailingSlash)
            end--;

        var path = start < end ? text[start..end] : string.Empty;
        return (path, (uint)text.Length, trailingSlash);
    }

    private static List<string> SplitSegments(string path)
    {
        if (string.IsNullOrEmpty(path))
            return [];

        return path.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static void NormalizeSegments(string parentDisplayPath, IList<string> segments)
    {
        var parentDepth = string.IsNullOrEmpty(parentDisplayPath)
            ? 0
            : parentDisplayPath.Count(c => c == '\\') + 1;

        for (var i = 0; i < segments.Count; i++)
        {
            var depth = parentDepth + i + 1;
            if (depth != 2)
                continue;

            var segment = segments[i];
            if (segment.Length >= 1 && char.IsLetter(segment[0]))
                segments[i] = char.ToUpperInvariant(segment[0]).ToString();
        }
    }

    private static nint BuildRelativePidl(IReadOnlyList<string> segments)
    {
        nint combined = 0;
        foreach (var segment in segments)
        {
            var simple = PidlHelper.CreateSimple(segment);
            if (combined == 0)
            {
                combined = simple;
                continue;
            }

            var next = PidlHelper.Concatenate(combined, simple);
            PidlHelper.Free(combined);
            combined = next;
        }

        return combined;
    }

    private static bool TryResolveTarget(
        string parentDisplayPath,
        IReadOnlyList<string> segments,
        bool validate,
        out XboxShellItem item)
    {
        item = default!;
        var currentPath = parentDisplayPath;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (!TryFindChild(currentPath, segment, validate, out var child))
                return false;

            if (i == segments.Count - 1)
            {
                item = child;
                return true;
            }

            if (!child.IsDirectory)
                return false;

            currentPath = child.FullPath;
        }

        return false;
    }

    private static bool TryFindChild(
        string parentDisplayPath,
        string segment,
        bool validate,
        out XboxShellItem item)
    {
        item = default!;

        if (string.IsNullOrEmpty(parentDisplayPath))
        {
            if (string.Equals(segment, ShellConstants.AddConsoleSegment, StringComparison.OrdinalIgnoreCase))
            {
                item = XboxShellItemFactory.FromPath(ShellConstants.AddConsoleSegment);
                return true;
            }

            var store = new ShellExtensionConsoleStore();
            if (store.GetConsoleNames().Any(name =>
                    string.Equals(name, segment, StringComparison.OrdinalIgnoreCase)))
            {
                item = XboxShellItemFactory.FromPath(segment);
                return true;
            }

            return false;
        }

        var depth = parentDisplayPath.Count(c => c == '\\') + 1;
        if (depth == 1 &&
            segment.Length >= 1 &&
            char.IsLetter(segment[0]))
        {
            var letter = char.ToUpperInvariant(segment[0]).ToString();
            item = XboxShellItemFactory.FromPath(WirePathService.BuildDriveDisplayPath(parentDisplayPath, letter[0]));
            return true;
        }

        foreach (var child in XboxShellItemFactory.ListChildren(parentDisplayPath))
        {
            if (SegmentMatches(child, segment))
            {
                item = child;
                return true;
            }
        }

        return false;
    }

    private static bool SegmentMatches(XboxShellItem child, string segment)
    {
        if (string.Equals(child.Segment, segment, StringComparison.OrdinalIgnoreCase))
            return true;

        if (child.Kind != XboxItemKind.Volume || segment.Length == 0 || !char.IsLetter(segment[0]))
            return false;

        var letter = char.ToUpperInvariant(segment[0]).ToString();
        return string.Equals(child.Segment, letter, StringComparison.OrdinalIgnoreCase);
    }
}
