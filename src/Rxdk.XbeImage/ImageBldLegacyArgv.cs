namespace Rxdk.XbeImage;

/// <summary>
/// Normalizes legacy imagebld command-line tokens before parsing.
/// Converts '-' switch prefixes to '/', and joins <c>/SWITCH value</c> into <c>/SWITCH:value</c>.
/// </summary>
public static class ImageBldLegacyArgv
{
    private static readonly HashSet<string> ColonValueSwitches = new(StringComparer.OrdinalIgnoreCase)
    {
        "IN",
        "OUT",
        "NOPRELOAD",
        "STACK",
        "INITFLAGS",
        "VERSION",
        "TESTVERSION",
        "TESTREGION",
        "TESTMEDIATYPES",
        "TESTRATINGS",
        "TESTID",
        "TESTALTID",
        "TESTNAME",
        "TESTLANKEY",
        "TESTSIGNKEY",
        "TITLEIMAGE",
        "TITLEINFO",
        "DEFAULTSAVEIMAGE",
        "INSERTFILE",
        "UDCLUSTER",
    };

    public static string[] Expand(string[] args)
    {
        var expanded = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Length == 0)
            {
                expanded.Add(arg);
                continue;
            }

            if (arg[0] == '@')
            {
                expanded.Add(arg);
                continue;
            }

            if (arg[0] is '-' or '/')
            {
                var normalized = arg[0] == '-' ? '/' + arg[1..] : arg;
                var body = normalized[1..];

                if (body.Contains(':', StringComparison.Ordinal))
                {
                    expanded.Add(normalized);
                    continue;
                }

                if (ColonValueSwitches.Contains(body) &&
                    i + 1 < args.Length &&
                    !IsSwitchOrResponse(args[i + 1]))
                {
                    expanded.Add('/' + body + ':' + args[++i]);
                    continue;
                }

                expanded.Add(normalized);
                continue;
            }

            expanded.Add(arg);
        }

        return expanded.ToArray();
    }

    private static bool IsSwitchOrResponse(string arg) =>
        arg.Length > 0 && arg[0] is '-' or '/' or '@';
}
