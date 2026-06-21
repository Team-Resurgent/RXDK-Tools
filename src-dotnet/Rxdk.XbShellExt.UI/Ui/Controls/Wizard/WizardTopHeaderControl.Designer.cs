namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class WizardTopHeaderControl
{
    private Label headerTitle = null!;
    private Label headerSubtitle = null!;
    private PictureBox headerLogo = null!;

    private void InitializeComponent()
    {
        headerTitle = new Label();
        headerSubtitle = new Label();
        headerLogo = new PictureBox();
        ((System.ComponentModel.ISupportInitialize)headerLogo).BeginInit();
        SuspendLayout();
        // 
        // headerTitle
        // 
        headerTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        headerTitle.ForeColor = Color.FromArgb(0, 51, 153);
        headerTitle.Location = new Point(12, 8);
        headerTitle.Size = new Size(300, 18);
        headerTitle.TabIndex = 0;
        headerTitle.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // headerSubtitle
        // 
        headerSubtitle.Location = new Point(12, 28);
        headerSubtitle.Size = new Size(300, 22);
        headerSubtitle.TabIndex = 1;
        // 
        // headerLogo
        // 
        headerLogo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        headerLogo.Location = new Point(340, 6);
        headerLogo.Size = new Size(64, 44);
        headerLogo.SizeMode = PictureBoxSizeMode.CenterImage;
        headerLogo.TabIndex = 2;
        headerLogo.TabStop = false;
        // 
        // WizardTopHeaderControl
        // 
        BackColor = Color.White;
        Controls.Add(headerLogo);
        Controls.Add(headerSubtitle);
        Controls.Add(headerTitle);
        MinimumSize = new Size(400, WizardVisuals.InnerHeaderHeight);
        Name = "WizardTopHeaderControl";
        Size = new Size(380, WizardVisuals.InnerHeaderHeight);
        ((System.ComponentModel.ISupportInitialize)headerLogo).EndInit();
        ResumeLayout(false);
    }
}
