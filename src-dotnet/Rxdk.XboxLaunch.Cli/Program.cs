using Rxdk.XboxLaunch;

namespace Rxdk.XboxLaunch.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = XboxLaunchOptionsParser.Parse(args);
            using var runner = new XboxLaunchRunner();
            return (int)runner.Run(options);
        }
        catch (XboxLaunchUsageException)
        {
            PrintUsage();
            return (int)XboxLaunchExitCode.Error;
        }
    }

    private static void PrintUsage()
    {
        var exe = Path.GetFileName(Environment.ProcessPath) ?? "xbox-launch";
        Console.Error.WriteLine($"usage: {exe} /dir xe:\\path /title game.xbe [/cmd args] [/x console] [/reboot] [/timeout ms]");
    }
}
