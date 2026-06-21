using Rxdk.XbShellExt.Ui;

namespace Rxdk.XbShellExt.Ui.Controls.Properties;

public sealed partial class ConsoleGeneralTab : UserControl
{
    public ConsoleGeneralTab()
    {
        InitializeComponent();
        SetFieldLabelFonts();
        DesignPreview.ApplyIfDesignTime(() =>
            Bind(
                DesignPreview.SampleConsoleName,
                DesignPreview.SampleConsoleIp,
                DesignPreview.SampleConsoleAltIp,
                DesignPreview.SampleRunningTitle));
    }

    public void Bind(string name, string ipAddress, string? altIpAddress, string runningTitle)
    {
        nameValue.Text = name;
        ipValue.Text = ipAddress;
        altIpRow.Visible = !string.IsNullOrWhiteSpace(altIpAddress);
        if (altIpRow.Visible)
            altIpValue.Text = altIpAddress!;
        runningTitleValue.Text = runningTitle;
    }

    private void SetFieldLabelFonts()
    {
        var bold = BoldFont();
        nameCaption.Font = bold;
        ipCaption.Font = bold;
        altIpCaption.Font = bold;
        runningTitleCaption.Font = bold;
    }

    private static Font BoldFont() =>
        new(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
}
