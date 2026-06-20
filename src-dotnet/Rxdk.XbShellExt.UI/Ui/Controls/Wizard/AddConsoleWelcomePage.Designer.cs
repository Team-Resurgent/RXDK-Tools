namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleWelcomePage
{
    private Label titleLabel = null!;
    private Label bodyLabel = null!;
    private Label continueLabel = null!;

    private void InitializeComponent()
    {
        titleLabel = new Label();
        bodyLabel = new Label();
        continueLabel = new Label();
        SuspendLayout();
        // 
        // titleLabel
        // 
        titleLabel.AutoSize = true;
        titleLabel.Location = new Point(0, 4);
        titleLabel.MaximumSize = new Size(360, 0);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(359, 50);
        titleLabel.TabIndex = 2;
        titleLabel.Text = "Welcome to the Add Xbox Development Kit Wizard";
        // 
        // bodyLabel
        // 
        bodyLabel.AutoSize = true;
        bodyLabel.Location = new Point(0, 36);
        bodyLabel.MaximumSize = new Size(360, 0);
        bodyLabel.Name = "bodyLabel";
        bodyLabel.Size = new Size(347, 50);
        bodyLabel.TabIndex = 1;
        bodyLabel.Text = "With this wizard, you may add an Xbox Development Kit to 'Xbox Neighborhood'.";
        // 
        // continueLabel
        // 
        continueLabel.AutoSize = true;
        continueLabel.Location = new Point(0, 68);
        continueLabel.MaximumSize = new Size(360, 0);
        continueLabel.Name = "continueLabel";
        continueLabel.Size = new Size(190, 25);
        continueLabel.TabIndex = 0;
        continueLabel.Text = "To continue, click Next.";
        // 
        // AddConsoleWelcomePage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        Controls.Add(continueLabel);
        Controls.Add(bodyLabel);
        Controls.Add(titleLabel);
        Name = "AddConsoleWelcomePage";
        Padding = new Padding(0, 4, 0, 0);
        Size = new Size(3287, 1487);
        ResumeLayout(false);
        PerformLayout();
    }
}
