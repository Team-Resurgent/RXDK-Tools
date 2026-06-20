namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleFinishPage
{
    private Label titleLabel = null!;
    private Label consoleCaption = null!;
    private Label consoleValueLabel = null!;
    private Label defaultCaption = null!;
    private Label defaultValueLabel = null!;
    private Label permissionsCaption = null!;
    private Label permissionsValueLabel = null!;
    private Label finishHelpLabel = null!;

    private void InitializeComponent()
    {
        titleLabel = new Label();
        consoleCaption = new Label();
        consoleValueLabel = new Label();
        defaultCaption = new Label();
        defaultValueLabel = new Label();
        permissionsCaption = new Label();
        permissionsValueLabel = new Label();
        finishHelpLabel = new Label();
        SuspendLayout();
        // 
        // titleLabel
        // 
        titleLabel.AutoSize = true;
        titleLabel.Location = new Point(0, 0);
        titleLabel.MaximumSize = new Size(360, 0);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(357, 50);
        titleLabel.TabIndex = 7;
        titleLabel.Text = "Completing the Add Xbox Development Kit Wizard";
        // 
        // consoleCaption
        // 
        consoleCaption.AutoSize = true;
        consoleCaption.Location = new Point(0, 36);
        consoleCaption.Name = "consoleCaption";
        consoleCaption.Size = new Size(233, 25);
        consoleCaption.TabIndex = 6;
        consoleCaption.Text = "Add Xbox Development Kit:";
        // 
        // consoleValueLabel
        // 
        consoleValueLabel.AutoSize = true;
        consoleValueLabel.Location = new Point(112, 36);
        consoleValueLabel.MaximumSize = new Size(248, 0);
        consoleValueLabel.Name = "consoleValueLabel";
        consoleValueLabel.Size = new Size(0, 25);
        consoleValueLabel.TabIndex = 5;
        // 
        // defaultCaption
        // 
        defaultCaption.AutoSize = true;
        defaultCaption.Location = new Point(0, 56);
        defaultCaption.Name = "defaultCaption";
        defaultCaption.Size = new Size(167, 25);
        defaultCaption.TabIndex = 4;
        defaultCaption.Text = "Make Default Xbox:";
        // 
        // defaultValueLabel
        // 
        defaultValueLabel.AutoSize = true;
        defaultValueLabel.Location = new Point(112, 56);
        defaultValueLabel.MaximumSize = new Size(248, 0);
        defaultValueLabel.Name = "defaultValueLabel";
        defaultValueLabel.Size = new Size(0, 25);
        defaultValueLabel.TabIndex = 3;
        // 
        // permissionsCaption
        // 
        permissionsCaption.AutoSize = true;
        permissionsCaption.Location = new Point(0, 76);
        permissionsCaption.Name = "permissionsCaption";
        permissionsCaption.Size = new Size(149, 25);
        permissionsCaption.TabIndex = 2;
        permissionsCaption.Text = "New Permissions:";
        // 
        // permissionsValueLabel
        // 
        permissionsValueLabel.AutoSize = true;
        permissionsValueLabel.Location = new Point(112, 76);
        permissionsValueLabel.MaximumSize = new Size(248, 0);
        permissionsValueLabel.Name = "permissionsValueLabel";
        permissionsValueLabel.Size = new Size(0, 25);
        permissionsValueLabel.TabIndex = 1;
        // 
        // finishHelpLabel
        // 
        finishHelpLabel.AutoSize = true;
        finishHelpLabel.Location = new Point(0, 104);
        finishHelpLabel.MaximumSize = new Size(360, 0);
        finishHelpLabel.Name = "finishHelpLabel";
        finishHelpLabel.Size = new Size(305, 50);
        finishHelpLabel.TabIndex = 0;
        finishHelpLabel.Text = "Click on Finish to perform the above changes.";
        // 
        // AddConsoleFinishPage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        Controls.Add(finishHelpLabel);
        Controls.Add(permissionsValueLabel);
        Controls.Add(permissionsCaption);
        Controls.Add(defaultValueLabel);
        Controls.Add(defaultCaption);
        Controls.Add(consoleValueLabel);
        Controls.Add(consoleCaption);
        Controls.Add(titleLabel);
        Name = "AddConsoleFinishPage";
        Size = new Size(3287, 1487);
        ResumeLayout(false);
        PerformLayout();
    }
}
