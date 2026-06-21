namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleWelcomePage
{
    private TableLayoutPanel layout = null!;
    private WizardSideBannerControl sideBanner = null!;
    private Panel contentPanel = null!;
    private Label titleLabel = null!;
    private Label bodyLabel = null!;
    private Label continueLabel = null!;
    private Label statusLabel = null!;

    private void InitializeComponent()
    {
        layout = new TableLayoutPanel();
        sideBanner = new WizardSideBannerControl();
        contentPanel = new Panel();
        continueLabel = new Label();
        bodyLabel = new Label();
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
        contentPanel.Controls.Add(continueLabel);
        contentPanel.Controls.Add(bodyLabel);
        contentPanel.Controls.Add(titleLabel);
        contentPanel.Controls.Add(statusLabel);
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Location = new Point(167, 3);
        contentPanel.Name = "contentPanel";
        contentPanel.Padding = new Padding(8, 10, 12, 0);
        contentPanel.Size = new Size(374, 305);
        contentPanel.TabIndex = 1;
        // 
        // continueLabel
        // 
        continueLabel.AutoSize = true;
        continueLabel.Location = new Point(8, 122);
        continueLabel.MaximumSize = new Size(360, 0);
        continueLabel.Name = "continueLabel";
        continueLabel.Size = new Size(130, 15);
        continueLabel.TabIndex = 2;
        continueLabel.Text = "To continue, click Next.";
        // 
        // bodyLabel
        // 
        bodyLabel.AutoSize = true;
        bodyLabel.Location = new Point(8, 66);
        bodyLabel.MaximumSize = new Size(360, 0);
        bodyLabel.Name = "bodyLabel";
        bodyLabel.Size = new Size(351, 30);
        bodyLabel.TabIndex = 1;
        bodyLabel.Text = "With this wizard, you may add an Xbox Development Kit to 'Xbox Neighborhood'.";
        // 
        // titleLabel
        // 
        titleLabel.AutoSize = true;
        titleLabel.Location = new Point(8, 10);
        titleLabel.MaximumSize = new Size(360, 0);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(275, 15);
        titleLabel.TabIndex = 0;
        titleLabel.Text = "Welcome to the Add Xbox Development Kit Wizard";
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
        statusLabel.TabIndex = 3;
        // 
        // AddConsoleWelcomePage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = SystemColors.Window;
        Controls.Add(layout);
        Name = "AddConsoleWelcomePage";
        Size = new Size(544, 311);
        layout.ResumeLayout(false);
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        ResumeLayout(false);
    }
}
