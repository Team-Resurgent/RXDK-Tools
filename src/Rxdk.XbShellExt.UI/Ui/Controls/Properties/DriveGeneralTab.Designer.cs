using Rxdk.XbShellExt.Ui.Controls;

namespace Rxdk.XbShellExt.Ui.Controls.Properties;

partial class DriveGeneralTab
{
    private Label descriptionLabel = null!;
    private Label typeCaption = null!;
    private Label typeValue = null!;
    private DriveUsagePieControl usagePie = null!;
    private Panel usedLegendSwatch = null!;
    private Label usedLegendLabel = null!;
    private Panel freeLegendSwatch = null!;
    private Label freeLegendLabel = null!;
    private Label usedCaption = null!;
    private Label usedValue = null!;
    private Label freeCaption = null!;
    private Label freeValue = null!;
    private Label capacityCaption = null!;
    private Label capacityValue = null!;

    private void InitializeComponent()
    {
        descriptionLabel = new Label();
        typeCaption = new Label();
        typeValue = new Label();
        usagePie = new DriveUsagePieControl();
        usedLegendSwatch = new Panel();
        usedLegendLabel = new Label();
        freeLegendSwatch = new Panel();
        freeLegendLabel = new Label();
        usedCaption = new Label();
        usedValue = new Label();
        freeCaption = new Label();
        freeValue = new Label();
        capacityCaption = new Label();
        capacityValue = new Label();
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Top;
        AutoSize = true;
        Padding = new Padding(0, 4, 0, 0);

        descriptionLabel.AutoSize = true;
        descriptionLabel.Location = new Point(0, 4);
        descriptionLabel.MaximumSize = new Size(400, 0);

        typeCaption.AutoSize = true;
        typeCaption.Location = new Point(0, 28);
        typeCaption.Text = "Type";

        typeValue.AutoSize = true;
        typeValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 28);
        typeValue.MaximumSize = new Size(340, 0);

        usagePie.Location = new Point(0, 52);
        usagePie.MinimumSize = new Size(140, 140);
        usagePie.Size = new Size(140, 140);
        usagePie.Margin = new Padding(0, 0, 16, 0);
        usagePie.UsedPercent = 62.5;

        descriptionLabel.Text = "Hard Disk (C:)";
        typeValue.Text = "Hard Disk Drive";
        usedValue.Text = "5,368,709,120 bytes (5.00 GB)";
        freeValue.Text = "3,221,225,472 bytes (3.00 GB)";
        capacityValue.Text = "8,589,934,592 bytes (8.00 GB)";

        usedLegendSwatch.BackColor = DriveUsagePieControl.UsedColor;
        usedLegendSwatch.BorderStyle = BorderStyle.FixedSingle;
        usedLegendSwatch.Location = new Point(156, 58);
        usedLegendSwatch.Size = new Size(14, 14);

        usedLegendLabel.AutoSize = true;
        usedLegendLabel.Location = new Point(176, 56);
        usedLegendLabel.Text = "Used space";

        freeLegendSwatch.BackColor = DriveUsagePieControl.FreeColor;
        freeLegendSwatch.BorderStyle = BorderStyle.FixedSingle;
        freeLegendSwatch.Location = new Point(156, 78);
        freeLegendSwatch.Size = new Size(14, 14);

        freeLegendLabel.AutoSize = true;
        freeLegendLabel.Location = new Point(176, 76);
        freeLegendLabel.Text = "Free space";

        usedCaption.AutoSize = true;
        usedCaption.Location = new Point(0, 200);
        usedCaption.Text = "Used";

        usedValue.AutoSize = true;
        usedValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 200);
        usedValue.MaximumSize = new Size(340, 0);

        freeCaption.AutoSize = true;
        freeCaption.Location = new Point(0, 222);
        freeCaption.Text = "Free";

        freeValue.AutoSize = true;
        freeValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 222);
        freeValue.MaximumSize = new Size(340, 0);

        capacityCaption.AutoSize = true;
        capacityCaption.Location = new Point(0, 244);
        capacityCaption.Text = "Capacity";

        capacityValue.AutoSize = true;
        capacityValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 244);
        capacityValue.MaximumSize = new Size(340, 0);

        Controls.Add(capacityValue);
        Controls.Add(capacityCaption);
        Controls.Add(freeValue);
        Controls.Add(freeCaption);
        Controls.Add(usedValue);
        Controls.Add(usedCaption);
        Controls.Add(freeLegendLabel);
        Controls.Add(freeLegendSwatch);
        Controls.Add(usedLegendLabel);
        Controls.Add(usedLegendSwatch);
        Controls.Add(usagePie);
        Controls.Add(typeValue);
        Controls.Add(typeCaption);
        Controls.Add(descriptionLabel);
        ResumeLayout(false);
        PerformLayout();
    }
}
