namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleFinishPage
{
    private TableLayoutPanel layout = null!;
    private WizardSideBannerControl sideBanner = null!;
    private Panel contentPanel = null!;
    private Label titleLabel = null!;
    private Label consoleCaption = null!;
    private Label consoleValueLabel = null!;
    private Label defaultCaption = null!;
    private Label defaultValueLabel = null!;
    private Label permissionsCaption = null!;
    private Label permissionsValueLabel = null!;
    private Label finishHelpLabel = null!;
    private Label statusLabel = null!;

    private void InitializeComponent()
    {
        layout = new TableLayoutPanel();
        sideBanner = new WizardSideBannerControl();
        contentPanel = new Panel();
        finishHelpLabel = new Label();
        permissionsValueLabel = new Label();
        permissionsCaption = new Label();
        defaultValueLabel = new Label();
        defaultCaption = new Label();
        consoleValueLabel = new Label();
        consoleCaption = new Label();
        titleLabel = new Label();
        statusLabel = new Label();
        layout.SuspendLayout();
        contentPanel.SuspendLayout();
        SuspendLayout();
        // 
        // layout
        // 
        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(sideBanner, 0, 0);
        layout.Controls.Add(contentPanel, 1, 0);
        layout.Dock = DockStyle.Fill;
        layout.Location = new Point(0, 0);
        layout.Margin = new Padding(0);
        layout.Name = "layout";
        layout.RowCount = 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Size = new Size(544, 311);
        layout.TabIndex = 0;
        // 
        // sideBanner
        // 
        sideBanner.BackColor = Color.White;
        sideBanner.Dock = DockStyle.Fill;
        sideBanner.Location = new Point(0, 0);
        sideBanner.Margin = new Padding(0);
        sideBanner.MinimumSize = new Size(164, 0);
        sideBanner.Name = "sideBanner";
        sideBanner.Size = new Size(164, 311);
        sideBanner.TabIndex = 0;
        // 
        // contentPanel
        // 
        contentPanel.BackColor = SystemColors.Window;
        contentPanel.Controls.Add(finishHelpLabel);
        contentPanel.Controls.Add(permissionsValueLabel);
        contentPanel.Controls.Add(permissionsCaption);
        contentPanel.Controls.Add(defaultValueLabel);
        contentPanel.Controls.Add(defaultCaption);
        contentPanel.Controls.Add(consoleValueLabel);
        contentPanel.Controls.Add(consoleCaption);
        contentPanel.Controls.Add(titleLabel);
        contentPanel.Controls.Add(statusLabel);
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Location = new Point(167, 3);
        contentPanel.Name = "contentPanel";
        contentPanel.Padding = new Padding(8, 10, 12, 0);
        contentPanel.Size = new Size(374, 305);
        contentPanel.TabIndex = 1;
        // 
        // finishHelpLabel
        // 
        finishHelpLabel.AutoSize = true;
        finishHelpLabel.Location = new Point(8, 134);
        finishHelpLabel.MaximumSize = new Size(360, 0);
        finishHelpLabel.Name = "finishHelpLabel";
        finishHelpLabel.Size = new Size(249, 15);
        finishHelpLabel.TabIndex = 7;
        finishHelpLabel.Text = "Click on Finish to perform the above changes.";
        // 
        // permissionsValueLabel
        // 
        permissionsValueLabel.AutoSize = true;
        permissionsValueLabel.Location = new Point(120, 106);
        permissionsValueLabel.MaximumSize = new Size(248, 0);
        permissionsValueLabel.Name = "permissionsValueLabel";
        permissionsValueLabel.Size = new Size(0, 15);
        permissionsValueLabel.TabIndex = 6;
        // 
        // permissionsCaption
        // 
        permissionsCaption.AutoSize = true;
        permissionsCaption.Location = new Point(8, 106);
        permissionsCaption.Name = "permissionsCaption";
        permissionsCaption.Size = new Size(100, 15);
        permissionsCaption.TabIndex = 5;
        permissionsCaption.Text = "New Permissions:";
        // 
        // defaultValueLabel
        // 
        defaultValueLabel.AutoSize = true;
        defaultValueLabel.Location = new Point(120, 86);
        defaultValueLabel.MaximumSize = new Size(248, 0);
        defaultValueLabel.Name = "defaultValueLabel";
        defaultValueLabel.Size = new Size(0, 15);
        defaultValueLabel.TabIndex = 4;
        // 
        // defaultCaption
        // 
        defaultCaption.AutoSize = true;
        defaultCaption.Location = new Point(8, 86);
        defaultCaption.Name = "defaultCaption";
        defaultCaption.Size = new Size(109, 15);
        defaultCaption.TabIndex = 3;
        defaultCaption.Text = "Make Default Xbox:";
        // 
        // consoleValueLabel
        // 
        consoleValueLabel.AutoSize = true;
        consoleValueLabel.Location = new Point(120, 66);
        consoleValueLabel.MaximumSize = new Size(248, 0);
        consoleValueLabel.Name = "consoleValueLabel";
        consoleValueLabel.Size = new Size(0, 15);
        consoleValueLabel.TabIndex = 2;
        // 
        // consoleCaption
        // 
        consoleCaption.AutoSize = true;
        consoleCaption.Location = new Point(8, 66);
        consoleCaption.Name = "consoleCaption";
        consoleCaption.Size = new Size(152, 15);
        consoleCaption.TabIndex = 1;
        consoleCaption.Text = "Add Xbox Development Kit:";
        // 
        // titleLabel
        // 
        titleLabel.AutoSize = true;
        titleLabel.Location = new Point(8, 10);
        titleLabel.MaximumSize = new Size(360, 0);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(274, 15);
        titleLabel.TabIndex = 0;
        titleLabel.Text = "Completing the Add Xbox Development Kit Wizard";
        // 
        // statusLabel
        // 
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.ForeColor = Color.DarkRed;
        statusLabel.Location = new Point(8, 286);
        statusLabel.MaximumSize = new Size(360, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.Padding = new Padding(0, 0, 0, 4);
        statusLabel.Size = new Size(0, 19);
        statusLabel.TabIndex = 8;
        // 
        // AddConsoleFinishPage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = SystemColors.Window;
        Controls.Add(layout);
        Name = "AddConsoleFinishPage";
        Size = new Size(544, 311);
        layout.ResumeLayout(false);
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        ResumeLayout(false);
    }
}
