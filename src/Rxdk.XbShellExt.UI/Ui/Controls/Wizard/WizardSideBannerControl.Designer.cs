namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

partial class WizardSideBannerControl
{
    private PictureBox bannerPicture = null!;

    private void InitializeComponent()
    {
        bannerPicture = new PictureBox();
        ((System.ComponentModel.ISupportInitialize)bannerPicture).BeginInit();
        SuspendLayout();
        // 
        // bannerPicture
        // 
        bannerPicture.Dock = DockStyle.Fill;
        bannerPicture.SizeMode = PictureBoxSizeMode.StretchImage;
        bannerPicture.TabIndex = 0;
        bannerPicture.TabStop = false;
        // 
        // WizardSideBannerControl
        // 
        BackColor = Color.White;
        Controls.Add(bannerPicture);
        MinimumSize = new Size(WizardVisuals.BannerWidth, 0);
        Name = "WizardSideBannerControl";
        Size = new Size(WizardVisuals.BannerWidth, 311);
        ((System.ComponentModel.ISupportInitialize)bannerPicture).EndInit();
        ResumeLayout(false);
    }
}
