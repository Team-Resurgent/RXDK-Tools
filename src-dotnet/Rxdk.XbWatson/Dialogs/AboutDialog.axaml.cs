using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Rxdk.XbWatson.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var version = typeof(AboutDialog).Assembly.GetName().Version?.ToString() ?? "1.0";
        AboutText.Text = $"Microsoft (R) xbWatson\nRXDK Build {version}\nCopyright (C) Microsoft Corp.";
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Rxdk.XbWatson/Assets/about.bmp"));
            AboutImage.Source = new Bitmap(stream);
        }
        catch
        {
            AboutImage.IsVisible = false;
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
