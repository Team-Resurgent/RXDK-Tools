using Rxdk.Xbdm.KitServices.Models;

namespace Rxdk.XbShellExt.Ui.Controls.Properties;

using FormattingHelper = Rxdk.Xbdm.KitServices.Services.FormattingHelper;

public sealed partial class FileGeneralTab : UserControl
{
    public FileGeneralTab()
    {
        InitializeComponent();
        attributesCaption.Font = BoldFont();
    }

    public TextBox? NameTextBox => nameTextBox.Visible ? nameTextBox : null;
    public CheckBox? ReadOnlyCheck => readOnlyCheck.Visible ? readOnlyCheck : null;
    public CheckBox? HiddenCheck => hiddenCheck.Visible ? hiddenCheck : null;

    public void Bind(FileGeneralInfo file, PropertyTargetKind kind, string caption)
    {
        var singleItem = kind is PropertyTargetKind.File or PropertyTargetKind.Folder;
        nameTextBox.Visible = singleItem;
        nameLabel.Visible = !singleItem;
        if (singleItem)
            nameTextBox.Text = file.DisplayName ?? caption;
        else
            nameLabel.Text = file.DisplayName ?? caption;

        typeRow.Visible = !string.IsNullOrWhiteSpace(file.TypeName);
        if (typeRow.Visible)
        {
            typeCaption.Text = kind == PropertyTargetKind.File ? "Type of file:" : "Type:";
            typeValue.Text = file.TypeName!;
        }

        locationRow.Visible = !string.IsNullOrWhiteSpace(file.Location);
        if (locationRow.Visible)
            locationValue.Text = file.Location!;

        sizeRow.Visible = file.TotalSize > 0 || kind == PropertyTargetKind.File;
        if (sizeRow.Visible)
            sizeValue.Text = FormattingHelper.FormatFileSize(file.TotalSize);

        containsRow.Visible = kind is PropertyTargetKind.Folder or PropertyTargetKind.MultiFile;
        if (containsRow.Visible)
            containsValue.Text = $"{file.FileCount} file(s), {file.FolderCount} folder(s)";

        createdRow.Visible = file.Created.HasValue;
        if (createdRow.Visible)
            createdValue.Text = FormattingHelper.FormatDateTime(file.Created);

        modifiedRow.Visible = file.Modified.HasValue;
        if (modifiedRow.Visible)
            modifiedValue.Text = FormattingHelper.FormatDateTime(file.Modified);

        readOnlyCheck.Visible = file.ReadOnly.HasValue || file.ValidAttributes != 0;
        if (readOnlyCheck.Visible)
        {
            readOnlyCheck.ThreeState = !file.ReadOnly.HasValue;
            if (file.ReadOnly.HasValue)
                readOnlyCheck.Checked = file.ReadOnly == true;
        }

        hiddenCheck.Visible = file.Hidden.HasValue || file.ValidAttributes != 0;
        if (hiddenCheck.Visible)
        {
            hiddenCheck.ThreeState = !file.Hidden.HasValue;
            if (file.Hidden.HasValue)
                hiddenCheck.Checked = file.Hidden == true;
        }

        attributesPanel.Visible = readOnlyCheck.Visible || hiddenCheck.Visible;
        PerformLayout();
    }

    private static Font BoldFont() =>
        new(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
}
