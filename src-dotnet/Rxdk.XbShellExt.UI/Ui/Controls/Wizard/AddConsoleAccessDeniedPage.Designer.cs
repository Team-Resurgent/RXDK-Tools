namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleAccessDeniedPage
{
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

    private void InitializeComponent()
    {
        passwordCaption = new Label();
        passwordTextBox = new TextBox();
        permissionsGroup = new GroupBox();
        privManageCheck = new CheckBox();
        privControlCheck = new CheckBox();
        privConfigureCheck = new CheckBox();
        privWriteCheck = new CheckBox();
        privReadCheck = new CheckBox();
        deniedHelpLabel = new Label();
        effectHelpLabel = new Label();
        permissionsGroup.SuspendLayout();
        SuspendLayout();
        // 
        // passwordCaption
        // 
        passwordCaption.Location = new Point(0, 0);
        passwordCaption.Name = "passwordCaption";
        passwordCaption.Size = new Size(148, 22);
        passwordCaption.TabIndex = 4;
        passwordCaption.Text = "Administrator &Password:";
        passwordCaption.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // passwordTextBox
        // 
        passwordTextBox.Location = new Point(152, 0);
        passwordTextBox.Name = "passwordTextBox";
        passwordTextBox.Size = new Size(180, 31);
        passwordTextBox.TabIndex = 3;
        passwordTextBox.UseSystemPasswordChar = true;
        // 
        // permissionsGroup
        // 
        permissionsGroup.Controls.Add(privManageCheck);
        permissionsGroup.Controls.Add(privControlCheck);
        permissionsGroup.Controls.Add(privConfigureCheck);
        permissionsGroup.Controls.Add(privWriteCheck);
        permissionsGroup.Controls.Add(privReadCheck);
        permissionsGroup.Location = new Point(0, 30);
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
        privManageCheck.Size = new Size(102, 29);
        privManageCheck.TabIndex = 0;
        privManageCheck.Text = "Manage";
        // 
        // privControlCheck
        // 
        privControlCheck.AutoSize = true;
        privControlCheck.Location = new Point(96, 36);
        privControlCheck.Name = "privControlCheck";
        privControlCheck.Size = new Size(97, 29);
        privControlCheck.TabIndex = 1;
        privControlCheck.Text = "Control";
        // 
        // privConfigureCheck
        // 
        privConfigureCheck.AutoSize = true;
        privConfigureCheck.Location = new Point(8, 36);
        privConfigureCheck.Name = "privConfigureCheck";
        privConfigureCheck.Size = new Size(116, 29);
        privConfigureCheck.TabIndex = 2;
        privConfigureCheck.Text = "Configure";
        // 
        // privWriteCheck
        // 
        privWriteCheck.AutoSize = true;
        privWriteCheck.Location = new Point(96, 18);
        privWriteCheck.Name = "privWriteCheck";
        privWriteCheck.Size = new Size(80, 29);
        privWriteCheck.TabIndex = 3;
        privWriteCheck.Text = "Write";
        // 
        // privReadCheck
        // 
        privReadCheck.AutoSize = true;
        privReadCheck.Location = new Point(8, 18);
        privReadCheck.Name = "privReadCheck";
        privReadCheck.Size = new Size(77, 29);
        privReadCheck.TabIndex = 4;
        privReadCheck.Text = "Read";
        // 
        // deniedHelpLabel
        // 
        deniedHelpLabel.AutoSize = true;
        deniedHelpLabel.Location = new Point(0, 110);
        deniedHelpLabel.MaximumSize = new Size(380, 0);
        deniedHelpLabel.Name = "deniedHelpLabel";
        deniedHelpLabel.Size = new Size(361, 125);
        deniedHelpLabel.TabIndex = 1;
        deniedHelpLabel.Text = "If you do not know the Administrator password, you may not proceed. You may click Back to specify a different Xbox Development Kit or you may click Cancel to exit this wizard.";
        // 
        // effectHelpLabel
        // 
        effectHelpLabel.AutoSize = true;
        effectHelpLabel.Location = new Point(0, 168);
        effectHelpLabel.MaximumSize = new Size(380, 0);
        effectHelpLabel.Name = "effectHelpLabel";
        effectHelpLabel.Size = new Size(379, 75);
        effectHelpLabel.TabIndex = 0;
        effectHelpLabel.Text = "Changes to access permissions will take effect only when you click Finish on the last page of this wizard.";
        // 
        // AddConsoleAccessDeniedPage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        Controls.Add(effectHelpLabel);
        Controls.Add(deniedHelpLabel);
        Controls.Add(permissionsGroup);
        Controls.Add(passwordTextBox);
        Controls.Add(passwordCaption);
        Name = "AddConsoleAccessDeniedPage";
        Size = new Size(3287, 1487);
        permissionsGroup.ResumeLayout(false);
        permissionsGroup.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
