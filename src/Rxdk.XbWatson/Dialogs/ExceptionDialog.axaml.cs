using Avalonia.Controls;
using Avalonia.Interactivity;
using Rxdk.XbWatson.Core;

namespace Rxdk.XbWatson.Dialogs;

public partial class ExceptionDialog : Window
{
    private readonly WatsonExceptionEvent _event = null!;
    private readonly Func<uint, uint, uint, bool, uint, string?, Task<bool>> _saveDump = null!;

    public ExceptionDialog()
    {
        InitializeComponent();
    }

    public ExceptionDialog(WatsonExceptionEvent evt, Func<uint, uint, uint, bool, uint, string?, Task<bool>> saveDump) : this()
    {
        _event = evt;
        _saveDump = saveDump;
        Title = $"An exception has occurred [{evt.ConsoleName}]";
        var (line1, line2) = WatsonExceptionFormatter.Format(evt.Code, evt.Address, evt.WriteViolation, evt.FaultAddress);
        Line1Text.Text = line1;
        Line2Text.Text = line2;
    }

    private async void OnDump(object? sender, RoutedEventArgs e)
    {
        var saved = await _saveDump(
            _event.ThreadId,
            WatsonDialogIds.Exception,
            _event.Code,
            _event.WriteViolation,
            (uint)_event.FaultAddress,
            null);
        if (saved)
            SavedText.IsVisible = true;
    }

    private void OnReboot(object? sender, RoutedEventArgs e) => Close(WatsonExceptionChoice.Reboot);
    private void OnContinue(object? sender, RoutedEventArgs e) => Close(WatsonExceptionChoice.Continue);
}
