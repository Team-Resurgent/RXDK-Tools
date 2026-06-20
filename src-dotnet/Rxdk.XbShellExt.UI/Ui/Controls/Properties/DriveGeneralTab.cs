using Rxdk.Xbdm.KitServices.Models;
using FormattingHelper = Rxdk.Xbdm.KitServices.Services.FormattingHelper;

namespace Rxdk.XbShellExt.Ui.Controls.Properties;

public sealed partial class DriveGeneralTab : UserControl
{
    public DriveGeneralTab()
    {
        InitializeComponent();
        descriptionLabel.Font = BoldFont();
        typeCaption.Font = BoldFont();
        usedCaption.Font = BoldFont();
        freeCaption.Font = BoldFont();
        capacityCaption.Font = BoldFont();
    }

    public void Bind(DriveGeneralInfo drive)
    {
        descriptionLabel.Text = drive.Description;
        typeValue.Text = drive.DriveType;
        usagePie.UsedPercent = drive.UsedPercent;
        usagePie.Invalidate();
        usedValue.Text = $"{FormattingHelper.FormatFileSizeBytes(drive.UsedBytes)} ({FormattingHelper.FormatFileSize(drive.UsedBytes)})";
        freeValue.Text = $"{FormattingHelper.FormatFileSizeBytes(drive.FreeBytes)} ({FormattingHelper.FormatFileSize(drive.FreeBytes)})";
        capacityValue.Text = $"{FormattingHelper.FormatFileSizeBytes(drive.TotalBytes)} ({FormattingHelper.FormatFileSize(drive.TotalBytes)})";
    }

    private static Font BoldFont() =>
        new(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
}
