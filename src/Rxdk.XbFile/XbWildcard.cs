namespace Rxdk.XbFile;

internal static class XbWildcard
{
    public static bool IsWildcard(string name) =>
        name.Contains('*') || name.Contains('?');

    public static bool IsMatch(string pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.IsNullOrEmpty(name);

        return Match(pattern.AsSpan(), name.AsSpan());
    }

    private static bool Match(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name)
    {
        var starIdx = pattern.IndexOf('*');
        if (starIdx < 0)
        {
            if (pattern.Length != name.Length)
                return false;

            for (var i = 0; i < pattern.Length; i++)
            {
                var p = pattern[i];
                if (p != '?' && char.ToUpperInvariant(p) != char.ToUpperInvariant(name[i]))
                    return false;
            }

            return true;
        }

        var prefix = pattern[..starIdx];
        if (!MatchPrefix(prefix, name))
            return false;

        pattern = pattern[(starIdx + 1)..];
        name = name[prefix.Length..];

        if (pattern.IsEmpty)
            return true;

        for (var skip = 0; skip <= name.Length; skip++)
        {
            if (Match(pattern, name[skip..]))
                return true;
        }

        return false;
    }

    private static bool MatchPrefix(ReadOnlySpan<char> prefix, ReadOnlySpan<char> name)
    {
        if (prefix.Length > name.Length)
            return false;

        for (var i = 0; i < prefix.Length; i++)
        {
            var p = prefix[i];
            if (p == '?')
                continue;
            if (char.ToUpperInvariant(p) != char.ToUpperInvariant(name[i]))
                return false;
        }

        return true;
    }
}
