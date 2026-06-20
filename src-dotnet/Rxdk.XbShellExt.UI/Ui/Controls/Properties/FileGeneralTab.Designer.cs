namespace Rxdk.XbShellExt.Ui.Controls.Properties;

partial class FileGeneralTab
{
    private PictureBox iconPicture = null!;
    private TextBox nameTextBox = null!;
    private Label nameLabel = null!;
    private Label separator1 = null!;
    private Panel typeRow = null!;
    private Label typeCaption = null!;
    private Label typeValue = null!;
    private Panel locationRow = null!;
    private Label locationCaption = null!;
    private Label locationValue = null!;
    private Panel sizeRow = null!;
    private Label sizeCaption = null!;
    private Label sizeValue = null!;
    private Panel containsRow = null!;
    private Label containsCaption = null!;
    private Label containsValue = null!;
    private Panel createdRow = null!;
    private Label createdCaption = null!;
    private Label createdValue = null!;
    private Panel modifiedRow = null!;
    private Label modifiedCaption = null!;
    private Label modifiedValue = null!;
    private Label separator2 = null!;
    private Panel attributesPanel = null!;
    private Label attributesCaption = null!;
    private CheckBox readOnlyCheck = null!;
    private CheckBox hiddenCheck = null!;

    private void InitializeComponent()
    {
        iconPicture = new PictureBox();
        nameTextBox = new TextBox();
        nameLabel = new Label();
        separator1 = new Label();
        typeRow = new Panel();
        typeCaption = new Label();
        typeValue = new Label();
        locationRow = new Panel();
        locationCaption = new Label();
        locationValue = new Label();
        sizeRow = new Panel();
        sizeCaption = new Label();
        sizeValue = new Label();
        containsRow = new Panel();
        containsCaption = new Label();
        containsValue = new Label();
        createdRow = new Panel();
        createdCaption = new Label();
        createdValue = new Label();
        modifiedRow = new Panel();
        modifiedCaption = new Label();
        modifiedValue = new Label();
        separator2 = new Label();
        attributesPanel = new Panel();
        attributesCaption = new Label();
        readOnlyCheck = new CheckBox();
        hiddenCheck = new CheckBox();
        typeRow.SuspendLayout();
        locationRow.SuspendLayout();
        sizeRow.SuspendLayout();
        containsRow.SuspendLayout();
        createdRow.SuspendLayout();
        modifiedRow.SuspendLayout();
        attributesPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)iconPicture).BeginInit();
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Top;
        AutoSize = true;
        Padding = new Padding(0, 4, 0, 0);

        iconPicture.Location = new Point(0, 4);
        iconPicture.Size = new Size(32, 32);
        iconPicture.SizeMode = PictureBoxSizeMode.CenterImage;
        iconPicture.Image = SystemIcons.Application.ToBitmap();

        nameTextBox.Location = new Point(40, 8);
        nameTextBox.Size = new Size(360, 23);

        nameLabel.AutoSize = true;
        nameLabel.Location = new Point(40, 12);
        nameLabel.MaximumSize = new Size(340, 0);
        nameLabel.Visible = false;

        separator1.BorderStyle = BorderStyle.Fixed3D;
        separator1.Location = new Point(0, 42);
        separator1.Size = new Size(400, 2);

        ConfigureFieldRow(typeRow, typeCaption, typeValue, "Type of file:", 48);
        ConfigureFieldRow(locationRow, locationCaption, locationValue, "Location:", 74);
        ConfigureFieldRow(sizeRow, sizeCaption, sizeValue, "Size:", 100);
        ConfigureFieldRow(containsRow, containsCaption, containsValue, "Contains:", 126);
        ConfigureFieldRow(createdRow, createdCaption, createdValue, "Created:", 152);
        ConfigureFieldRow(modifiedRow, modifiedCaption, modifiedValue, "Modified:", 178);

        nameTextBox.Text = "Halo.xbe";
        typeValue.Text = "Xbox Executable";
        typeRow.Visible = true;
        locationValue.Text = @"myxbox\C\Games";
        locationRow.Visible = true;
        sizeValue.Text = "4.00 MB";
        sizeRow.Visible = true;
        createdValue.Text = "Tuesday, November 09, 2004 2:30:00 PM";
        createdRow.Visible = true;
        modifiedValue.Text = "Tuesday, March 15, 2005 9:12:00 AM";
        modifiedRow.Visible = true;
        attributesPanel.Visible = true;

        separator2.BorderStyle = BorderStyle.Fixed3D;
        separator2.Location = new Point(0, 204);
        separator2.Size = new Size(400, 2);

        attributesPanel.Controls.Add(hiddenCheck);
        attributesPanel.Controls.Add(readOnlyCheck);
        attributesPanel.Controls.Add(attributesCaption);
        attributesPanel.Location = new Point(0, 214);
        attributesPanel.Size = new Size(400, 72);

        attributesCaption.AutoSize = true;
        attributesCaption.Location = new Point(0, 4);
        attributesCaption.Text = "Attributes:";

        readOnlyCheck.AutoSize = true;
        readOnlyCheck.Location = new Point(48, 24);
        readOnlyCheck.Text = "Read-only";

        hiddenCheck.AutoSize = true;
        hiddenCheck.Location = new Point(48, 46);
        hiddenCheck.Text = "Hidden";

        Controls.Add(attributesPanel);
        Controls.Add(separator2);
        Controls.Add(modifiedRow);
        Controls.Add(createdRow);
        Controls.Add(containsRow);
        Controls.Add(sizeRow);
        Controls.Add(locationRow);
        Controls.Add(typeRow);
        Controls.Add(separator1);
        Controls.Add(nameLabel);
        Controls.Add(nameTextBox);
        Controls.Add(iconPicture);
        attributesPanel.ResumeLayout(false);
        attributesPanel.PerformLayout();
        modifiedRow.ResumeLayout(false);
        modifiedRow.PerformLayout();
        createdRow.ResumeLayout(false);
        createdRow.PerformLayout();
        containsRow.ResumeLayout(false);
        containsRow.PerformLayout();
        sizeRow.ResumeLayout(false);
        sizeRow.PerformLayout();
        locationRow.ResumeLayout(false);
        locationRow.PerformLayout();
        typeRow.ResumeLayout(false);
        typeRow.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)iconPicture).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private static void ConfigureFieldRow(Panel row, Label caption, Label value, string captionText, int top)
    {
        row.Controls.Add(value);
        row.Controls.Add(caption);
        row.Location = new Point(0, top);
        row.Size = new Size(400, 22);
        row.Visible = false;

        caption.AutoSize = true;
        caption.Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
        caption.Location = new Point(0, 2);
        caption.Text = captionText;

        value.AutoSize = true;
        value.Location = new Point(ShellDialogLayout.FieldLabelWidth, 2);
        value.MaximumSize = new Size(340, 0);
    }
}
