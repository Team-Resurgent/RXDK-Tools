#nullable enable
namespace Rxdk.XbShellExt.Ui.Controls;

partial class ShellWizardChrome
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel layout = null!;
    private Panel bannerPanel = null!;
    private PictureBox bannerPicture = null!;
    private Panel rightPanel = null!;
    private Panel headerPanel = null!;
    private Panel headerSeparator = null!;
    private Label headerTitle = null!;
    private Label headerSubtitle = null!;
    private PictureBox headerLogo = null!;
    private Panel contentHost = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        layout = new TableLayoutPanel();
        bannerPanel = new Panel();
        bannerPicture = new PictureBox();
        rightPanel = new Panel();
        contentHost = new Panel();
        headerSeparator = new Panel();
        headerPanel = new Panel();
        headerTitle = new Label();
        headerSubtitle = new Label();
        headerLogo = new PictureBox();
        layout.SuspendLayout();
        bannerPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)bannerPicture).BeginInit();
        rightPanel.SuspendLayout();
        headerPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)headerLogo).BeginInit();
        SuspendLayout();

        Dock = DockStyle.Fill;

        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BannerWidth));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Dock = DockStyle.Fill;
        layout.Margin = Padding.Empty;
        layout.Padding = Padding.Empty;
        layout.RowCount = 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        bannerPanel.BackColor = Color.White;
        bannerPanel.Controls.Add(bannerPicture);
        bannerPanel.Dock = DockStyle.Fill;
        bannerPanel.Margin = Padding.Empty;

        bannerPicture.Dock = DockStyle.Fill;
        bannerPicture.SizeMode = PictureBoxSizeMode.StretchImage;

        rightPanel.BackColor = SystemColors.Window;
        rightPanel.Controls.Add(contentHost);
        rightPanel.Controls.Add(headerSeparator);
        rightPanel.Controls.Add(headerPanel);
        rightPanel.Dock = DockStyle.Fill;
        rightPanel.Margin = Padding.Empty;

        contentHost.BackColor = SystemColors.Window;
        contentHost.Dock = DockStyle.Fill;
        contentHost.Padding = new Padding(12, 10, 12, 8);

        headerSeparator.BackColor = SystemColors.ControlDark;
        headerSeparator.Dock = DockStyle.Top;
        headerSeparator.Height = 1;
        headerSeparator.Visible = false;

        headerPanel.BackColor = Color.White;
        headerPanel.Controls.Add(headerSubtitle);
        headerPanel.Controls.Add(headerTitle);
        headerPanel.Controls.Add(headerLogo);
        headerPanel.Dock = DockStyle.Top;
        headerPanel.Height = InnerHeaderHeight;
        headerPanel.Visible = false;

        headerTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        headerTitle.ForeColor = Color.FromArgb(0, 51, 153);
        headerTitle.Location = new Point(12, 8);
        headerTitle.Size = new Size(300, 18);
        headerTitle.TextAlign = ContentAlignment.MiddleLeft;

        headerSubtitle.Location = new Point(12, 28);
        headerSubtitle.Size = new Size(300, 22);

        headerLogo.Location = new Point(340, 6);
        headerLogo.Size = new Size(64, 44);
        headerLogo.SizeMode = PictureBoxSizeMode.CenterImage;

        layout.Controls.Add(bannerPanel, 0, 0);
        layout.Controls.Add(rightPanel, 1, 0);
        Controls.Add(layout);

        headerPanel.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)headerLogo).EndInit();
        rightPanel.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)bannerPicture).EndInit();
        bannerPanel.ResumeLayout(false);
        layout.ResumeLayout(false);
        ResumeLayout(false);
    }
}
