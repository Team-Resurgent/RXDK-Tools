using System.CommandLine;
using Rxdk.XbFile;

namespace Rxdk.XbMkdir;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var directory = new Argument<string>("directory")
        {
            Description = "Directory to create (xE:\\..., xD:\\..., or PC path).",
        };

        var xbox = new Option<string?>(["-x", "/x", "--xbox"])
        {
            Description = "Xbox hostname or IP address.",
        };
        var ensureParents = new Option<bool>(["/t", "-t"])
        {
            Description = "Create parent directories as needed.",
        };

        var root = new RootCommand("Create a directory on the PC or Xbox target system.")
        {
            directory, xbox, ensureParents,
        };

        root.SetHandler(Execute, directory, xbox, ensureParents);

        try
        {
            return await root.InvokeAsync(XbLegacyArgv.ForXbMkdir(args));
        }
        catch (XbFileException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static void Execute(string directory, string? xbox, bool ensureParents)
    {
        try
        {
            var path = XbPath.Parse(directory);
            using var session = path.IsXbox || !string.IsNullOrWhiteSpace(xbox)
                ? XbConsoleSession.Connect(xbox)
                : null;

            new XbMkdirService().Execute(path, ensureParents, session);
        }
        catch (XbFileException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
