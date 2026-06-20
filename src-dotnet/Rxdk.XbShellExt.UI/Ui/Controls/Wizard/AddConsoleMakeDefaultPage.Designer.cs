namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class AddConsoleMakeDefaultPage
{
    private Label promptLabel = null!;
    private RadioButton makeDefaultYes = null!;
    private RadioButton makeDefaultNo = null!;

    private void InitializeComponent()
    {
        promptLabel = new Label();
        makeDefaultYes = new RadioButton();
        makeDefaultNo = new RadioButton();
        SuspendLayout();
        // 
        // promptLabel
        // 
        promptLabel.AutoSize = true;
        promptLabel.Location = new Point(0, 0);
        promptLabel.MaximumSize = new Size(380, 0);
        promptLabel.Name = "promptLabel";
        promptLabel.Size = new Size(0, 25);
        promptLabel.TabIndex = 2;
        // 
        // makeDefaultYes
        // 
        makeDefaultYes.AutoSize = true;
        makeDefaultYes.Location = new Point(0, 28);
        makeDefaultYes.Name = "makeDefaultYes";
        makeDefaultYes.Size = new Size(62, 29);
        makeDefaultYes.TabIndex = 1;
        makeDefaultYes.Text = "&Yes";
        // 
        // makeDefaultNo
        // 
        makeDefaultNo.AutoSize = true;
        makeDefaultNo.Location = new Point(0, 50);
        makeDefaultNo.Name = "makeDefaultNo";
        makeDefaultNo.Size = new Size(61, 29);
        makeDefaultNo.TabIndex = 0;
        makeDefaultNo.Text = "N&o";
        // 
        // AddConsoleMakeDefaultPage
        // 
        AutoScaleMode = AutoScaleMode.Inherit;
        Controls.Add(makeDefaultNo);
        Controls.Add(makeDefaultYes);
        Controls.Add(promptLabel);
        Name = "AddConsoleMakeDefaultPage";
        Size = new Size(3287, 1487);
        ResumeLayout(false);
        PerformLayout();
    }
}
