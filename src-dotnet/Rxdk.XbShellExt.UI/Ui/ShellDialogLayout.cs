namespace Rxdk.XbShellExt.Ui;

internal static class ShellDialogLayout
{
    public static readonly Size StandardButtonSize = new(88, 30);
    public static readonly Size WizardButtonSize = new(96, 30);
    public const int ButtonBarHeight = 52;
    public const int ButtonBarPaddingTop = 10;
    public const int ButtonBarPaddingRight = 12;
    public const int ButtonBarPaddingBottom = 10;
    public const int ButtonSpacing = 8;
    /// <summary>Width for property-sheet label column (fits "Modified:", "Location:", etc.).</summary>
    public const int FieldLabelWidth = 120;

    public static void ConfigureButton(Button button, Size? size = null)
    {
        button.AutoSize = false;
        button.Size = size ?? StandardButtonSize;
    }

    public static Panel CreateWizardButtonBar(params Button[] buttons) =>
        CreateButtonBar(WizardButtonSize, buttons);

    public static Panel CreateButtonBar(params Button[] buttons) =>
        CreateButtonBar(StandardButtonSize, buttons);

    public static Panel CreateButtonBar(Size buttonSize, params Button[] buttons)
    {
        foreach (var button in buttons)
            ConfigureButton(button, buttonSize);

        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = ButtonBarHeight,
            Padding = new Padding(0, ButtonBarPaddingTop, ButtonBarPaddingRight, ButtonBarPaddingBottom),
        };

        foreach (var button in buttons)
            panel.Controls.Add(button);

        panel.Resize += (_, _) => PositionButtons(panel, buttons);
        PositionButtons(panel, buttons);
        return panel;
    }

    public static void PositionButtons(Panel buttonPanel, params Button[] buttons)
    {
        if (buttons.Length == 0)
            return;

        var right = buttonPanel.ClientSize.Width - buttonPanel.Padding.Right;
        var top = buttonPanel.Padding.Top;

        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            button.Location = new Point(right - button.Width, top);
            right = button.Left - ButtonSpacing;
        }
    }
}
