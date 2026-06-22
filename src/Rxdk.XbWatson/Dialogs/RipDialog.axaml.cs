using Avalonia.Controls;
using Avalonia.Interactivity;
using Rxdk.XbWatson.Core;

namespace Rxdk.XbWatson.Dialogs;

public partial class RipDialog : Window
{
    private readonly WatsonRipEvent _event = null!;
    private readonly Func<uint, uint, uint, bool, uint, string?, Task<bool>> _saveDump = null!;

    public RipDialog()
    {
        InitializeComponent();
    }

    public RipDialog(WatsonRipEvent evt, Func<uint, uint, uint, bool, uint, string?, Task<bool>> saveDump) : this()
    {
        _event = evt;
        _saveDump = saveDump;
        Title = $"An exception has occurred [{evt.ConsoleName}]";
        CaptionText.Text = "A fatal error has occurred on the Xbox";
        BodyText.Text = $"A RIP error has occurred on the Xbox\nThe error description was: {evt.RipText}";
    }

    private async void OnDump(object? sender, RoutedEventArgs e)
    {
        var saved = await _saveDump(
            _event.ThreadId,
            WatsonDialogIds.Rip,
            0,
            false,
            0,
            _event.RipText);
        if (saved)
            SavedText.IsVisible = true;
    }

    private void OnReboot(object? sender, RoutedEventArgs e) => Close(WatsonRipChoice.Reboot);
    private void OnBreak(object? sender, RoutedEventArgs e) => Close(WatsonRipChoice.Break);
    private void OnContinue(object? sender, RoutedEventArgs e) => Close(WatsonRipChoice.Continue);
}
