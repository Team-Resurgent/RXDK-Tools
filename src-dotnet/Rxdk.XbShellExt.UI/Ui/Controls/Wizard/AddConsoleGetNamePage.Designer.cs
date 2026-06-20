namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleGetNamePage
{
    private Label introLabel = null!;
    private Label nameCaption = null!;
    private TextBox nameTextBox = null!;
    private Label helpLabel = null!;

    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AddConsoleGetNamePage));
        introLabel = new Label();
        nameCaption = new Label();
        nameTextBox = new TextBox();
        helpLabel = new Label();
        SuspendLayout();
        // 
        // introLabel
        // 
        introLabel.AutoSize = true;
        introLabel.Location = new Point(0, 0);
        introLabel.MaximumSize = new Size(380, 0);
        introLabel.Name = "introLabel";
        introLabel.Size = new Size(376, 50);
        introLabel.TabIndex = 3;
        introLabel.Text = "Specify an Xbox Development Kit.  When you are done, click Next.";
        // 
        // nameCaption
        // 
        nameCaption.Location = new Point(0, 28);
        nameCaption.Name = "nameCaption";
        nameCaption.Size = new Size(148, 22);
        nameCaption.TabIndex = 2;
        nameCaption.Text = "Xbox n&ame or IP address:";
        nameCaption.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // nameTextBox
        // 
        nameTextBox.Location = new Point(152, 28);
        nameTextBox.Name = "nameTextBox";
        nameTextBox.Size = new Size(180, 31);
        nameTextBox.TabIndex = 1;
        // 
        // helpLabel
        // 
        helpLabel.AutoSize = true;
        helpLabel.Location = new Point(0, 58);
        helpLabel.MaximumSize = new Size(380, 0);
        helpLabel.Name = "helpLabel";
        helpLabel.Size = new Size(379, 150);
        helpLabel.TabIndex = 0;
        helpLabel.Text = resources.GetString("helpLabel.Text");
        // 
        // AddConsoleGetNamePage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        Controls.Add(helpLabel);
        Controls.Add(nameTextBox);
        Controls.Add(nameCaption);
        Controls.Add(introLabel);
        Name = "AddConsoleGetNamePage";
        Size = new Size(3287, 1487);
        ResumeLayout(false);
        PerformLayout();
    }
}
