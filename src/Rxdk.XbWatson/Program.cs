using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;

namespace Rxdk.XbWatson;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var parsed = WatsonCommandLine.Parse(args);
        if (!parsed.Success)
        {
            Console.Error.WriteLine(parsed.ErrorMessage);
            Environment.Exit(-1);
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
