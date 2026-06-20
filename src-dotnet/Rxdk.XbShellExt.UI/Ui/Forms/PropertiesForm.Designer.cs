using Rxdk.XbShellExt.Ui.Controls.Properties;

namespace Rxdk.XbShellExt.Ui.Forms;

partial class PropertiesForm
{
    private TabControl tabs = null!;
    private TabPage designGeneralTabPage = null!;
    private FileGeneralTab designFileGeneralTab = null!;
    private Panel buttonPanel = null!;
    private Button applyButton = null!;
    private Button cancelButton = null!;
    private Button okButton = null!;

    private void InitializeComponent()
    {
        tabs = new TabControl();
        designFileGeneralTab = new FileGeneralTab();
        designGeneralTabPage = new TabPage();
        buttonPanel = new Panel();
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

        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.Height = ShellDialogLayout.ButtonBarHeight;
        buttonPanel.Padding = new Padding(0, ShellDialogLayout.ButtonBarPaddingTop, ShellDialogLayout.ButtonBarPaddingRight, ShellDialogLayout.ButtonBarPaddingBottom);

        applyButton.Enabled = false;
        applyButton.Text = "Apply";

        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Text = "Cancel";

        okButton.DialogResult = DialogResult.None;
        okButton.Text = "OK";

        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(applyButton);

        Controls.Add(tabs);
        Controls.Add(buttonPanel);

        buttonPanel.ResumeLayout(false);
        designGeneralTabPage.ResumeLayout(false);
        tabs.ResumeLayout(false);
        ResumeLayout(false);
    }
}
