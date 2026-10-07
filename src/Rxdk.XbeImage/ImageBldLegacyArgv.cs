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

            // A POSIX absolute path (e.g. the positional input/output of /DXT on
            // Linux/macOS) starts with '/' but is not a switch: pass it through
            // untouched so it is never normalized or joined onto a prior switch.
            if (arg[0] == '/' && LooksLikePosixPath(arg))
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
        arg.Length > 0 && arg[0] is '-' or '/' or '@' && !LooksLikePosixPath(arg);

    /// <summary>
    /// True when the argument is a POSIX absolute path rather than a legacy
    /// '/'-switch. A colon-value switch is "/NAME:VALUE" (the VALUE may itself be a
    /// POSIX path, e.g. "/in:/home/x") and a plain switch is "/NAME" ("/DXT"); in
    /// both the ':' (if any) comes before any further '/'. A POSIX absolute path
    /// ("/home/x") has a '/' before any ':'. So it is a path exactly when a second
    /// '/' appears and precedes the first ':'. OS-agnostic: a Windows path is never
    /// passed as a bare "/dir/..." token, so this never misfires on Windows.
    /// </summary>
    public static bool LooksLikePosixPath(string arg)
    {
        if (arg.Length < 2 || arg[0] != '/')
            return false;
        var slash = arg.IndexOf('/', 1);
        if (slash < 0)
            return false;
        var colon = arg.IndexOf(':', 1);
        return colon < 0 || slash < colon;
    }
}
