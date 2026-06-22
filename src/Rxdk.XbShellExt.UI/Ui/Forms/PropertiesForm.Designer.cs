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
        designGeneralTabPage = new TabPage();
        designFileGeneralTab = new FileGeneralTab();
        buttonPanel = new FlowLayoutPanel();
        applyButton = new Button();
        cancelButton = new Button();
        okButton = new Button();
        tabs.SuspendLayout();
        designGeneralTabPage.SuspendLayout();
        buttonPanel.SuspendLayout();
        SuspendLayout();
        // 
        // tabs
        // 
        tabs.Controls.Add(designGeneralTabPage);
        tabs.Dock = DockStyle.Fill;
        tabs.Location = new Point(0, 0);
        tabs.Name = "tabs";
        tabs.SelectedIndex = 0;
        tabs.Size = new Size(440, 438);
        tabs.TabIndex = 0;
        // 
        // designGeneralTabPage
        // 
        designGeneralTabPage.Controls.Add(designFileGeneralTab);
        designGeneralTabPage.Location = new Point(4, 24);
        designGeneralTabPage.Name = "designGeneralTabPage";
        designGeneralTabPage.Padding = new Padding(12);
        designGeneralTabPage.Size = new Size(432, 410);
        designGeneralTabPage.TabIndex = 0;
        designGeneralTabPage.Text = "General";
        designGeneralTabPage.UseVisualStyleBackColor = true;
        // 
        // designFileGeneralTab
        // 
        designFileGeneralTab.AutoSize = true;
        designFileGeneralTab.Dock = DockStyle.Fill;
        designFileGeneralTab.Location = new Point(12, 12);
        designFileGeneralTab.Name = "designFileGeneralTab";
        designFileGeneralTab.Padding = new Padding(0, 4, 0, 0);
        designFileGeneralTab.Size = new Size(408, 386);
        designFileGeneralTab.TabIndex = 0;
        // 
        // buttonPanel
        // 
        buttonPanel.Controls.Add(applyButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonPanel.Location = new Point(0, 438);
        buttonPanel.Margin = new Padding(0);
        buttonPanel.Name = "buttonPanel";
        buttonPanel.Padding = new Padding(8);
        buttonPanel.Size = new Size(440, 52);
        buttonPanel.TabIndex = 1;
        buttonPanel.WrapContents = false;
        // 
        // applyButton
        // 
        applyButton.Enabled = false;
        applyButton.Location = new Point(328, 8);
        applyButton.Margin = new Padding(8, 0, 0, 0);
        applyButton.Name = "applyButton";
        applyButton.Size = new Size(96, 36);
        applyButton.TabIndex = 0;
        applyButton.Text = "Apply";
        // 
        // cancelButton
        // 
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(224, 8);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(96, 36);
        cancelButton.TabIndex = 1;
        cancelButton.Text = "Cancel";
        // 
        // okButton
        // 
        okButton.Location = new Point(120, 8);
        okButton.Margin = new Padding(8, 0, 0, 0);
        okButton.Name = "okButton";
        okButton.Size = new Size(96, 36);
        okButton.TabIndex = 2;
        okButton.Text = "OK";
        // 
        // PropertiesForm
        // 
        AcceptButton = okButton;
        AutoScaleMode = AutoScaleMode.Dpi;
        CancelButton = cancelButton;
        ClientSize = new Size(440, 490);
        Controls.Add(tabs);
        Controls.Add(buttonPanel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "PropertiesForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Halo.xbe Properties";
        tabs.ResumeLayout(false);
        designGeneralTabPage.ResumeLayout(false);
        designGeneralTabPage.PerformLayout();
        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
