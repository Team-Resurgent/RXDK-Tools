namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleGetNamePage
{
    private Panel headerSeparator = null!;
    private WizardTopHeaderControl pageHeader = null!;
    private Panel contentPanel = null!;
    private Label introLabel = null!;
    private Label nameCaption = null!;
    private TextBox nameTextBox = null!;
    private Label helpLabel = null!;
    private Label statusLabel = null!;

    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AddConsoleGetNamePage));
        headerSeparator = new Panel();
        pageHeader = new WizardTopHeaderControl();
        contentPanel = new Panel();
        helpLabel = new Label();
        nameTextBox = new TextBox();
        nameCaption = new Label();
        introLabel = new Label();
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
        contentPanel.Controls.Add(helpLabel);
        contentPanel.Controls.Add(nameTextBox);
        contentPanel.Controls.Add(nameCaption);
        contentPanel.Controls.Add(introLabel);
        contentPanel.Controls.Add(statusLabel);
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Location = new Point(0, 58);
        contentPanel.Name = "contentPanel";
        contentPanel.Padding = new Padding(12, 10, 12, 0);
        contentPanel.Size = new Size(544, 253);
        contentPanel.TabIndex = 0;
        // 
        // helpLabel
        // 
        helpLabel.AutoSize = true;
        helpLabel.Location = new Point(12, 96);
        helpLabel.MaximumSize = new Size(380, 0);
        helpLabel.Name = "helpLabel";
        helpLabel.Size = new Size(379, 150);
        helpLabel.TabIndex = 3;
        helpLabel.Text = resources.GetString("helpLabel.Text");
        // 
        // nameTextBox
        // 
        nameTextBox.Location = new Point(164, 66);
        nameTextBox.Name = "nameTextBox";
        nameTextBox.Size = new Size(180, 31);
        nameTextBox.TabIndex = 2;
        // 
        // nameCaption
        // 
        nameCaption.Location = new Point(12, 66);
        nameCaption.Name = "nameCaption";
        nameCaption.Size = new Size(148, 22);
        nameCaption.TabIndex = 1;
        nameCaption.Text = "Xbox n&ame or IP address:";
        nameCaption.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // introLabel
        // 
        introLabel.AutoSize = true;
        introLabel.Location = new Point(12, 10);
        introLabel.MaximumSize = new Size(380, 0);
        introLabel.Name = "introLabel";
        introLabel.Size = new Size(376, 50);
        introLabel.TabIndex = 0;
        introLabel.Text = "Specify an Xbox Development Kit.  When you are done, click Next.";
        // 
        // statusLabel
        // 
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.ForeColor = Color.DarkRed;
        statusLabel.Location = new Point(12, 224);
        statusLabel.MaximumSize = new Size(380, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.Padding = new Padding(0, 0, 0, 4);
        statusLabel.Size = new Size(0, 29);
        statusLabel.TabIndex = 4;
        // 
        // AddConsoleGetNamePage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = SystemColors.Control;
        Controls.Add(contentPanel);
        Controls.Add(headerSeparator);
        Controls.Add(pageHeader);
        Name = "AddConsoleGetNamePage";
        Size = new Size(544, 311);
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        ResumeLayout(false);
    }
}
