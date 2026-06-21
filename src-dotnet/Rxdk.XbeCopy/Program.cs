using System.CommandLine;
using Rxdk.XbFile;

namespace Rxdk.XbeCopy;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var localSource = new Argument<string>("local-source")
        {
            Description = "Local file to copy to the Xbox.",
        };
        var remoteDest = new Argument<string>("remote-dest")
        {
            Description = "Xbox destination path (E:\\... or xE:\\...).",
        };
        var xbox = new Option<string?>(["-x", "/x", "--xbox"])
        {
            Description = "Xbox hostname or IP address.",
        };
        var noLogo = new Option<bool>(["/NOLOGO", "--nologo"])
        {
            Description = "Suppress copyright banner.",
        };

        var root = new RootCommand("Copy a local image/XBE to the Xbox (Visual Studio deploy helper).")
        {
            localSource, remoteDest, xbox, noLogo,
        };

        root.SetHandler(Execute, localSource, remoteDest, xbox, noLogo);

        try
        {
            return await root.InvokeAsync(XbLegacyArgv.ForXbeCopy(args));
        }
        catch (XbFileException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static void Execute(string localSource, string remoteDest, string? xbox, bool noLogo)
    {
        try
        {
            using var session = XbConsoleSession.Connect(xbox);
            new XbeCopyService().Execute(localSource, remoteDest, noLogo, session);
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
