using System.ComponentModel;
using Rxdk.XbShellExt.Ui;

namespace Rxdk.XbShellExt.Ui.Forms;

/// <summary>Thin WinForms base for shell dialogs. No runtime chrome here so the VS designer can load this type reliably.</summary>
public class ShellDialogForm : Form
{
    private float _appliedDpiFactor;

    internal void SetShellOwner(nint ownerHwnd) => ShellOwnerHwnd = ownerHwnd;

    internal nint ShellOwnerHwnd { get; private set; }

    /// <summary>96 DPI design-time client size, when the form should be scaled explicitly.</summary>
    protected virtual Size? ShellDesignClientSize => null;

    protected void ApplyRuntimeChrome()
    {
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            ShellModernChrome.Apply(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            ShellDialogDpi.ReleaseFixedSizeLocks(this);

        base.OnHandleCreated(e);
    }

    protected override void OnLoad(EventArgs e)
    {
        EnsureShellDpiScaled();
        base.OnLoad(e);
    }

    protected override void OnShown(EventArgs e)
    {
        EnsureShellDpiScaled();
        base.OnShown(e);
    }

    internal void EnsureShellDpiScaled()
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            return;

        var factor = ShellDialogDpi.ResolveDpi(ShellOwnerHwnd) / ShellDialogDpi.DesignDpi;
        if (factor <= 1.02f)
            return;

        if (_appliedDpiFactor > 0f && Math.Abs(_appliedDpiFactor - factor) < 0.02f)
            return;

        if (ShellDialogDpi.ApplyShellDpiScale(this, ShellOwnerHwnd, ShellDesignClientSize))
            _appliedDpiFactor = factor;
    }

    protected Size ScaleDesignSize(Size size) => ShellDialogDpi.ScaleDesignSize(this, size);
}
