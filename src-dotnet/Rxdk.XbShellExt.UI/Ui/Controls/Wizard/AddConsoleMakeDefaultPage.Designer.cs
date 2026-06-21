namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleMakeDefaultPage
{
    private Panel headerSeparator = null!;
    private WizardTopHeaderControl pageHeader = null!;
    private Panel contentPanel = null!;
    private Label promptLabel = null!;
    private RadioButton makeDefaultYes = null!;
    private RadioButton makeDefaultNo = null!;
    private Label statusLabel = null!;

    private void InitializeComponent()
    {
        headerSeparator = new Panel();
        pageHeader = new WizardTopHeaderControl();
        contentPanel = new Panel();
        makeDefaultNo = new RadioButton();
        makeDefaultYes = new RadioButton();
        promptLabel = new Label();
        statusLabel = new Label();
        contentPanel.SuspendLayout();
        SuspendLayout();
        // 
        // headerSeparator
        // 
        headerSeparator.BackColor = SystemColors.ControlDark;
        headerSeparator.Dock = DockStyle.Top;
        headerSeparator.Location = new Point(0, 57);
        headerSeparator.Name = "headerSeparator";
        headerSeparator.Size = new Size(544, 1);
        headerSeparator.TabIndex = 1;
        // 
        // pageHeader
        // 
        pageHeader.BackColor = Color.White;
        pageHeader.Dock = DockStyle.Top;
        pageHeader.HeaderSubtitle = "";
        pageHeader.HeaderTitle = "";
        pageHeader.Location = new Point(0, 0);
        pageHeader.MinimumSize = new Size(400, 57);
        pageHeader.Name = "pageHeader";
        pageHeader.Size = new Size(544, 57);
        pageHeader.TabIndex = 2;
        // 
        // contentPanel
        // 
        contentPanel.BackColor = SystemColors.Control;
        contentPanel.Controls.Add(makeDefaultNo);
        contentPanel.Controls.Add(makeDefaultYes);
        contentPanel.Controls.Add(promptLabel);
        contentPanel.Controls.Add(statusLabel);
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Location = new Point(0, 58);
        contentPanel.Name = "contentPanel";
        contentPanel.Padding = new Padding(12, 10, 12, 0);
        contentPanel.Size = new Size(544, 253);
        contentPanel.TabIndex = 0;
        // 
        // makeDefaultNo
        // 
        makeDefaultNo.AutoSize = true;
        makeDefaultNo.Location = new Point(12, 60);
        makeDefaultNo.Name = "makeDefaultNo";
        makeDefaultNo.Size = new Size(41, 19);
        makeDefaultNo.TabIndex = 2;
        makeDefaultNo.Text = "N&o";
        // 
        // makeDefaultYes
        // 
        makeDefaultYes.AutoSize = true;
        makeDefaultYes.Location = new Point(12, 38);
        makeDefaultYes.Name = "makeDefaultYes";
        makeDefaultYes.Size = new Size(42, 19);
        makeDefaultYes.TabIndex = 1;
        makeDefaultYes.Text = "&Yes";
        // 
        // promptLabel
        // 
        promptLabel.AutoSize = true;
        promptLabel.Location = new Point(12, 10);
        promptLabel.MaximumSize = new Size(380, 0);
        promptLabel.Name = "promptLabel";
        promptLabel.Size = new Size(0, 15);
        promptLabel.TabIndex = 0;
        // 
        // statusLabel
        // 
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.ForeColor = Color.DarkRed;
        statusLabel.Location = new Point(12, 234);
        statusLabel.MaximumSize = new Size(380, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.Padding = new Padding(0, 0, 0, 4);
        statusLabel.Size = new Size(0, 19);
        statusLabel.TabIndex = 3;
        // 
        // AddConsoleMakeDefaultPage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = SystemColors.Control;
        Controls.Add(contentPanel);
        Controls.Add(headerSeparator);
        Controls.Add(pageHeader);
        Name = "AddConsoleMakeDefaultPage";
        Size = new Size(544, 311);
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        ResumeLayout(false);
    }
}
