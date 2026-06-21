namespace Rxdk.XbFile;

/// <summary>
/// Expands legacy Xbox tool command lines to match original OPTH / manual parsers
/// before handing them to System.CommandLine.
/// </summary>
public static class XbLegacyArgv
{
    private static readonly HashSet<char> XbCpOptionChars = new("hrsfyqdetfHRSFYQDETF");
    private static readonly HashSet<char> XbDirOptionChars = new("hrsbwhHRSBWH");
    private static readonly HashSet<char> XbMkdirOptionChars = new("tT");

    public static string[] ForXbCp(string[] args) =>
        ExpandOptnStyle(args, XbCpOptionChars);

    public static string[] ForXbDir(string[] args) =>
        ExpandOptnStyle(args, XbDirOptionChars);

    public static string[] ForXbMkdir(string[] args) =>
        ExpandOptnStyle(args, XbMkdirOptionChars);

  /// <summary>
    /// xbecopy only treats '/' switches as options (not '-'). Supports /NOLOGO, /x, and -x.
    /// </summary>
    public static string[] ForXbeCopy(string[] args)
    {
        var expanded = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Length >= 2 && arg[0] == '/' && IsNoLogo(arg))
            {
                expanded.Add("/NOLOGO");
                continue;
            }

            if (IsXSwitch(arg))
            {
                if (i + 1 >= args.Length)
                    throw new XbFileException("Missing Xbox name after /x or -x.");

                expanded.Add(NormalizeXSwitch(arg));
                expanded.Add(args[++i]);
                continue;
            }

            if (arg.Length >= 2 && arg[0] == '/')
            {
                Console.Error.WriteLine($"warning: unrecognized option {arg}");
                continue;
            }

            expanded.Add(arg);
        }

        RemoveExtraPositionals(expanded, maxPositionals: 2, "extra argument");
        return expanded.ToArray();
    }

    private static string[] ExpandOptnStyle(string[] args, HashSet<char> validSingleCharOptions)
    {
        var expanded = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Length < 2 || arg[0] is not '/' and not '-')
            {
                expanded.Add(arg);
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                expanded.Add(arg);
                continue;
            }

            if (TryNormalizeSortOption(arg, out var sortOption))
            {
                expanded.Add(sortOption);
                continue;
            }

            var prefix = arg[0];
            var body = arg[1..];

            for (var j = 0; j < body.Length; j++)
            {
                var c = body[j];
                if (c is 'x' or 'X')
                {
                    expanded.Add(prefix + "x");
                    if (i + 1 >= args.Length)
                        throw new XbFileException("Missing Xbox name after /x or -x.");

                    expanded.Add(args[++i]);

                    for (var k = j + 1; k < body.Length; k++)
                        EmitSingleCharOption(expanded, prefix, body[k], validSingleCharOptions);

                    break;
                }

                EmitSingleCharOption(expanded, prefix, c, validSingleCharOptions);
            }
        }

        return expanded.ToArray();
    }

    private static void EmitSingleCharOption(
        List<string> expanded,
        char prefix,
        char optionChar,
        HashSet<char> validSingleCharOptions)
    {
        if (!validSingleCharOptions.Contains(optionChar))
            throw new XbFileException($"Unrecognized option '{prefix}{optionChar}'.");

        expanded.Add($"{prefix}{char.ToLowerInvariant(optionChar)}");
    }

    private static bool TryNormalizeSortOption(string arg, out string normalized)
    {
        if (arg.Length < 3)
        {
            normalized = arg;
            return false;
        }

        var prefix = arg[0];
        if (prefix is not '/' and not '-')
        {
            normalized = arg;
            return false;
        }

        if (arg[1] is not 'o' and not 'O')
        {
            normalized = arg;
            return false;
        }

        var idx = 2;
        var reverse = false;
        if (idx < arg.Length && arg[idx] == '-')
        {
            reverse = true;
            idx++;
        }

        if (idx >= arg.Length)
        {
            normalized = arg;
            return false;
        }

        var sort = arg[idx];
        if (sort is not ('n' or 'N' or 'd' or 'D' or 's' or 'S'))
        {
            normalized = arg;
            return false;
        }

        if (idx + 1 < arg.Length)
        {
            normalized = arg;
            return false;
        }

        normalized = prefix + "o" + (reverse ? "-" : "") + char.ToLowerInvariant(sort);
        return true;
    }

    private static bool IsNoLogo(string arg) =>
        arg.Length >= 2
        && arg[0] == '/'
        && arg.AsSpan(1).Equals("NOLOGO", StringComparison.OrdinalIgnoreCase);

    private static bool IsXSwitch(string arg) =>
        arg.Length == 2
        && arg[0] is '/' or '-'
        && arg[1] is 'x' or 'X';

    private static string NormalizeXSwitch(string arg) =>
        arg[0] + "x";

    private static bool IsXbeCopyOptionToken(string arg) =>
        IsNoLogo(arg) || IsXSwitch(arg);

    private static void RemoveExtraPositionals(List<string> expanded, int maxPositionals, string warningLabel)
    {
        var positionalCount = 0;
        for (var i = 0; i < expanded.Count; i++)
        {
            var token = expanded[i];
            if (IsXbeCopyOptionToken(token))
            {
                if (IsXSwitch(token))
                    i++;
                continue;
            }

            positionalCount++;
            if (positionalCount > maxPositionals)
            {
                Console.Error.WriteLine($"warning: {warningLabel} {token}");
                expanded.RemoveAt(i);
                i--;
            }
        }
    }
}
