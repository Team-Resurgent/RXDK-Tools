using Avalonia.Controls;
using Avalonia.Interactivity;
using Rxdk.XbWatson.Core;

namespace Rxdk.XbWatson.Dialogs;

public partial class AssertDialog : Window
{
    private readonly WatsonAssertEvent _event = null!;

    public AssertDialog()
    {
        InitializeComponent();
    }

    public AssertDialog(WatsonAssertEvent evt) : this()
    {
        _event = evt;
        var display = WatsonAssertParser.Parse(evt.AssertText, evt.LaunchPath);
        Title = $"Xbox Assertion Failed - {Path.GetFileName(display.TitleProgram)} [{evt.ConsoleName}]";

        if (display.IsAbortStyle)
        {
            HeaderText.Text = "Debug Error!";
            ProgramText.Text = display.TitleProgram;
            FileText.Text = string.Empty;
            LineText.Text = display.AbortLine1;
            ExpressionText.Text = display.AbortLine2 + "\n" + display.AbortLine3;
        }
        else
        {
            HeaderText.Text = "Assertion failed!";
            ProgramText.Text = display.TitleProgram;
            FileText.Text = display.File;
            LineText.Text = display.Line;
            ExpressionText.Text = display.Expression;
        }
    }

    private void OnReboot(object? sender, RoutedEventArgs e) => Close(WatsonAssertChoice.Reboot);
    private void OnBreak(object? sender, RoutedEventArgs e) => Close(WatsonAssertChoice.Break);
    private void OnContinue(object? sender, RoutedEventArgs e) => Close(WatsonAssertChoice.Continue);
}
