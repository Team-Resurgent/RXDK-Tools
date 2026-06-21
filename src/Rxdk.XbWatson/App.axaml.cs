using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Rxdk.KitConfig;

namespace Rxdk.XbWatson;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var parsed = WatsonCommandLine.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
            var config = KitConfigProvider.CreateDefault();
            var consoleName = parsed.ConsoleName ?? config.Consoles.GetDefaultConsoleName();
            if (string.IsNullOrWhiteSpace(consoleName))
            {
                ShowStartupError("No default Xbox console is configured. Use /x xboxname.");
                return;
            }

            if (parsed.SetDefaultConsole)
                config.Consoles.SetDefaultConsole(consoleName!);

            desktop.MainWindow = new MainWindow(consoleName!);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowStartupError(string message)
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialog = new Avalonia.Controls.Window
            {
                Title = "xbWatson",
                Width = 420,
                Height = 160,
                Content = new Avalonia.Controls.TextBlock
                {
                    Text = message,
                    Margin = new Avalonia.Thickness(16),
                },
            };
            desktop.MainWindow = dialog;
        }
    }
}
