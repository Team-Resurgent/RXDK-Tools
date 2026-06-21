namespace Rxdk.XbShellExt.Ui.Controls.Properties;

partial class ConsoleGeneralTab
{
    private Label nameCaption = null!;
    private Label nameValue = null!;
    private Label ipCaption = null!;
    private Label ipValue = null!;
    private Panel altIpRow = null!;
    private Label altIpCaption = null!;
    private Label altIpValue = null!;
    private Label runningTitleCaption = null!;
    private Label runningTitleValue = null!;

    private void InitializeComponent()
    {
        nameCaption = new Label();
        nameValue = new Label();
        ipCaption = new Label();
        ipValue = new Label();
        altIpRow = new Panel();
        altIpCaption = new Label();
        altIpValue = new Label();
        runningTitleCaption = new Label();
        runningTitleValue = new Label();
        altIpRow.SuspendLayout();
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Top;
        AutoSize = true;
        Padding = new Padding(0, 4, 0, 0);

        nameCaption.AutoSize = true;
        nameCaption.Location = new Point(0, 4);
        nameCaption.Text = "Name";

        nameValue.AutoSize = true;
        nameValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 4);
        nameValue.MaximumSize = new Size(340, 0);

        ipCaption.AutoSize = true;
        ipCaption.Location = new Point(0, 26);
        ipCaption.Text = "IP address";

        ipValue.AutoSize = true;
        ipValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 26);
        ipValue.MaximumSize = new Size(340, 0);

        altIpRow.Controls.Add(altIpValue);
        altIpRow.Controls.Add(altIpCaption);
        altIpRow.Location = new Point(0, 48);
        altIpRow.Size = new Size(460, 22);

        altIpCaption.AutoSize = true;
        altIpCaption.Location = new Point(0, 2);
        altIpCaption.Text = "Alternate IP";

        altIpValue.AutoSize = true;
        altIpValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 2);
        altIpValue.MaximumSize = new Size(340, 0);

        runningTitleCaption.AutoSize = true;
        runningTitleCaption.Location = new Point(0, 74);
        runningTitleCaption.Text = "Running title";

        runningTitleValue.AutoSize = true;
        runningTitleValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 74);
        runningTitleValue.MaximumSize = new Size(340, 0);

        Controls.Add(runningTitleValue);
        Controls.Add(runningTitleCaption);
        Controls.Add(altIpRow);
        Controls.Add(ipValue);
        Controls.Add(ipCaption);
        Controls.Add(nameValue);
        Controls.Add(nameCaption);
        altIpRow.ResumeLayout(false);
        altIpRow.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
