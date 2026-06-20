using Rxdk.XbShellExt.Ui.Controls.Properties;

namespace Rxdk.XbShellExt.Ui.Forms;

partial class PropertiesForm
{
    private TabControl tabs = null!;
    private TabPage designGeneralTabPage = null!;
    private FileGeneralTab designFileGeneralTab = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button applyButton = null!;
    private Button cancelButton = null!;
    private Button okButton = null!;

    private void InitializeComponent()
    {
        tabs = new TabControl();
        designFileGeneralTab = new FileGeneralTab();
        designGeneralTabPage = new TabPage();
        buttonPanel = new FlowLayoutPanel();
        applyButton = new Button();
        cancelButton = new Button();
        okButton = new Button();
        buttonPanel.SuspendLayout();
        designGeneralTabPage.SuspendLayout();
        tabs.SuspendLayout();
        SuspendLayout();

        Text = "Halo.xbe Properties";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 490);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        tabs.Dock = DockStyle.Fill;

        designGeneralTabPage.Padding = new Padding(12);
        designGeneralTabPage.Text = "General";
        designGeneralTabPage.UseVisualStyleBackColor = true;

        designFileGeneralTab.Dock = DockStyle.Fill;

        designGeneralTabPage.Controls.Add(designFileGeneralTab);
        tabs.Controls.Add(designGeneralTabPage);

        buttonPanel.AutoSize = false;
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonPanel.Height = ShellDialogLayout.ButtonBarHeight;
        buttonPanel.Margin = Padding.Empty;
        buttonPanel.Padding = new Padding(0, ShellDialogLayout.ButtonBarPaddingTop, ShellDialogLayout.ButtonBarPaddingRight, ShellDialogLayout.ButtonBarPaddingBottom);
        buttonPanel.WrapContents = false;

        applyButton.AutoSize = false;
        applyButton.Enabled = false;
        applyButton.Margin = new Padding(ShellDialogLayout.ButtonSpacing, 0, 0, 0);
        applyButton.Size = ShellDialogLayout.ButtonSize;
        applyButton.Text = "Apply";

        cancelButton.AutoSize = false;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Margin = new Padding(ShellDialogLayout.ButtonSpacing, 0, 0, 0);
        cancelButton.Size = ShellDialogLayout.ButtonSize;
        cancelButton.Text = "Cancel";

        okButton.AutoSize = false;
        okButton.DialogResult = DialogResult.None;
        okButton.Margin = new Padding(ShellDialogLayout.ButtonSpacing, 0, 0, 0);
        okButton.Size = ShellDialogLayout.ButtonSize;
        okButton.Text = "OK";

        buttonPanel.Controls.Add(applyButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        Controls.Add(tabs);
        Controls.Add(buttonPanel);

        buttonPanel.ResumeLayout(false);
        designGeneralTabPage.ResumeLayout(false);
        tabs.ResumeLayout(false);
        ResumeLayout(false);
    }
}
