using Rxdk.KitConfig;

namespace Rxdk.XbWatson;

public sealed class WatsonCommandLineResult
{
    public bool Success { get; init; }
    public string? ConsoleName { get; init; }
    public bool SetDefaultConsole { get; init; }
    public string? ErrorMessage { get; init; }
}

public static class WatsonCommandLine
{
    public static WatsonCommandLineResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            var config = KitConfigProvider.CreateDefault();
            return new WatsonCommandLineResult
            {
                Success = true,
                ConsoleName = config.Consoles.GetDefaultConsoleName(),
            };
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Length >= 2 && (arg[0] == '-' || arg[0] == '/') &&
                (arg[1] == 'x' || arg[1] == 'X'))
            {
                var name = arg.Length > 2 ? arg[2..].Trim() : null;
                if (string.IsNullOrWhiteSpace(name) && i + 1 < args.Length)
                    name = args[++i].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    return Invalid();

                return new WatsonCommandLineResult
                {
                    Success = true,
                    ConsoleName = name,
                    SetDefaultConsole = true,
                };
            }
        }

        return Invalid();
    }

    private static WatsonCommandLineResult Invalid() => new()
    {
        Success = false,
        ErrorMessage =
            "xbWatson.exe.\n\r\nusage: xbWatson [/x xboxname]\r\n        /x    Specify Xbox to explore.",
    };
}
