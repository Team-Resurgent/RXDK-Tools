using System.CommandLine;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbSet;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var targetArgument = new Argument<string?>("target")
        {
            Description = "Xbox hostname or IP address.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var xboxOption = new Option<string?>(["-x", "/x", "--xbox"])
        {
            Description = "Xbox hostname or IP address (same as positional target).",
        };

        var root = new RootCommand("Set the default Xbox development kit for XBDM tools.")
        {
            targetArgument,
            xboxOption,
        };

        root.SetHandler(Execute, targetArgument, xboxOption);

        return await root.InvokeAsync(args);
    }

    private static void Execute(string? target, string? xboxOption)
    {
        var connectTarget = xboxOption ?? target;
        if (string.IsNullOrWhiteSpace(connectTarget))
        {
            Console.Error.WriteLine("error: Xbox hostname or IP address is required.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("usage: xbset <hostname-or-ip>");
            Console.Error.WriteLine("       xbset /x <hostname-or-ip>");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            var result = new SetDefaultConsoleService().Execute(connectTarget);
            Console.WriteLine($"Default console set to '{result.RegistryName}'.");
            if (!string.IsNullOrWhiteSpace(result.IpAddress))
                Console.WriteLine($"Address: {result.IpAddress}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
