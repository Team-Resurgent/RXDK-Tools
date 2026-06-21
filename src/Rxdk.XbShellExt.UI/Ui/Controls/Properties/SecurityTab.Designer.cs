namespace Rxdk.XbShellExt.Ui.Controls.Properties;

partial class SecurityTab
{
    private Label securityCaption = null!;
    private Label securityValue = null!;
    private Label accessCaption = null!;
    private Label accessValue = null!;

    private void InitializeComponent()
    {
        securityCaption = new Label();
        securityValue = new Label();
        accessCaption = new Label();
        accessValue = new Label();
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Top;
        AutoSize = true;
        Padding = new Padding(0, 4, 0, 0);

        securityCaption.AutoSize = true;
        securityCaption.Location = new Point(0, 4);
        securityCaption.Text = "Security enabled";

        securityValue.AutoSize = true;
        securityValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 4);
        securityValue.MaximumSize = new Size(340, 0);

        accessCaption.AutoSize = true;
        accessCaption.Location = new Point(0, 26);
        accessCaption.Text = "Your access";

        accessValue.AutoSize = true;
        accessValue.Location = new Point(ShellDialogLayout.FieldLabelWidth, 26);
        accessValue.MaximumSize = new Size(340, 0);

        Controls.Add(accessValue);
        Controls.Add(accessCaption);
        Controls.Add(securityValue);
        Controls.Add(securityCaption);
        ResumeLayout(false);
        PerformLayout();
    }
}
