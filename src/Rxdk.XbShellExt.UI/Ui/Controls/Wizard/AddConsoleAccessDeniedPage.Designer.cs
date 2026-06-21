namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleAccessDeniedPage
{
    private Panel headerSeparator = null!;
    private WizardTopHeaderControl pageHeader = null!;
    private Panel contentPanel = null!;
    private Label passwordCaption = null!;
    private TextBox passwordTextBox = null!;
    private GroupBox permissionsGroup = null!;
    private CheckBox privReadCheck = null!;
    private CheckBox privWriteCheck = null!;
    private CheckBox privConfigureCheck = null!;
    private CheckBox privControlCheck = null!;
    private CheckBox privManageCheck = null!;
    private Label deniedHelpLabel = null!;
    private Label effectHelpLabel = null!;
    private Label statusLabel = null!;

    private void InitializeComponent()
    {
        headerSeparator = new Panel();
        pageHeader = new WizardTopHeaderControl();
        contentPanel = new Panel();
        effectHelpLabel = new Label();
        deniedHelpLabel = new Label();
        permissionsGroup = new GroupBox();
        privManageCheck = new CheckBox();
        privControlCheck = new CheckBox();
        privConfigureCheck = new CheckBox();
        privWriteCheck = new CheckBox();
        privReadCheck = new CheckBox();
        passwordTextBox = new TextBox();
        passwordCaption = new Label();
        statusLabel = new Label();
        contentPanel.SuspendLayout();
        permissionsGroup.SuspendLayout();
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
        contentPanel.Controls.Add(effectHelpLabel);
        contentPanel.Controls.Add(deniedHelpLabel);
        contentPanel.Controls.Add(permissionsGroup);
        contentPanel.Controls.Add(passwordTextBox);
        contentPanel.Controls.Add(passwordCaption);
        contentPanel.Controls.Add(statusLabel);
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Location = new Point(0, 58);
        contentPanel.Name = "contentPanel";
        contentPanel.Padding = new Padding(12, 10, 12, 0);
        contentPanel.Size = new Size(544, 253);
        contentPanel.TabIndex = 0;
        // 
        // effectHelpLabel
        // 
        effectHelpLabel.AutoSize = true;
        effectHelpLabel.Location = new Point(12, 178);
        effectHelpLabel.MaximumSize = new Size(380, 0);
        effectHelpLabel.Name = "effectHelpLabel";
        effectHelpLabel.Size = new Size(360, 30);
        effectHelpLabel.TabIndex = 4;
        effectHelpLabel.Text = "Changes to access permissions will take effect only when you click Finish on the last page of this wizard.";
        // 
        // deniedHelpLabel
        // 
        deniedHelpLabel.AutoSize = true;
        deniedHelpLabel.Location = new Point(12, 120);
        deniedHelpLabel.MaximumSize = new Size(380, 0);
        deniedHelpLabel.Name = "deniedHelpLabel";
        deniedHelpLabel.Size = new Size(379, 45);
        deniedHelpLabel.TabIndex = 3;
        deniedHelpLabel.Text = "If you do not know the Administrator password, you may not proceed. You may click Back to specify a different Xbox Development Kit or you may click Cancel to exit this wizard.";
        // 
        // permissionsGroup
        // 
        permissionsGroup.Controls.Add(privManageCheck);
        permissionsGroup.Controls.Add(privControlCheck);
        permissionsGroup.Controls.Add(privConfigureCheck);
        permissionsGroup.Controls.Add(privWriteCheck);
        permissionsGroup.Controls.Add(privReadCheck);
        permissionsGroup.Location = new Point(12, 40);
        permissionsGroup.Name = "permissionsGroup";
        permissionsGroup.Size = new Size(380, 72);
        permissionsGroup.TabIndex = 2;
        permissionsGroup.TabStop = false;
        permissionsGroup.Text = "Permissions:";
        // 
        // privManageCheck
        // 
        privManageCheck.AutoSize = true;
        privManageCheck.Location = new Point(8, 54);
        privManageCheck.Name = "privManageCheck";
        privManageCheck.Size = new Size(69, 19);
        privManageCheck.TabIndex = 0;
        privManageCheck.Text = "Manage";
        // 
        // privControlCheck
        // 
        privControlCheck.AutoSize = true;
        privControlCheck.Location = new Point(96, 36);
        privControlCheck.Name = "privControlCheck";
        privControlCheck.Size = new Size(66, 19);
        privControlCheck.TabIndex = 1;
        privControlCheck.Text = "Control";
        // 
        // privConfigureCheck
        // 
        privConfigureCheck.AutoSize = true;
        privConfigureCheck.Location = new Point(8, 36);
        privConfigureCheck.Name = "privConfigureCheck";
        privConfigureCheck.Size = new Size(79, 19);
        privConfigureCheck.TabIndex = 2;
        privConfigureCheck.Text = "Configure";
        // 
        // privWriteCheck
        // 
        privWriteCheck.AutoSize = true;
        privWriteCheck.Location = new Point(96, 18);
        privWriteCheck.Name = "privWriteCheck";
        privWriteCheck.Size = new Size(54, 19);
        privWriteCheck.TabIndex = 3;
        privWriteCheck.Text = "Write";
        // 
        // privReadCheck
        // 
        privReadCheck.AutoSize = true;
        privReadCheck.Location = new Point(8, 18);
        privReadCheck.Name = "privReadCheck";
        privReadCheck.Size = new Size(52, 19);
        privReadCheck.TabIndex = 4;
        privReadCheck.Text = "Read";
        // 
        // passwordTextBox
        // 
        passwordTextBox.Location = new Point(164, 10);
        passwordTextBox.Name = "passwordTextBox";
        passwordTextBox.Size = new Size(180, 23);
        passwordTextBox.TabIndex = 1;
        passwordTextBox.UseSystemPasswordChar = true;
        // 
        // passwordCaption
        // 
        passwordCaption.Location = new Point(12, 10);
        passwordCaption.Name = "passwordCaption";
        passwordCaption.Size = new Size(148, 22);
        passwordCaption.TabIndex = 0;
        passwordCaption.Text = "Administrator &Password:";
        passwordCaption.TextAlign = ContentAlignment.MiddleLeft;
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
        statusLabel.TabIndex = 5;
        // 
        // AddConsoleAccessDeniedPage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = SystemColors.Control;
        Controls.Add(contentPanel);
        Controls.Add(headerSeparator);
        Controls.Add(pageHeader);
        Name = "AddConsoleAccessDeniedPage";
        Size = new Size(544, 311);
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        permissionsGroup.ResumeLayout(false);
        permissionsGroup.PerformLayout();
        ResumeLayout(false);
    }
}
