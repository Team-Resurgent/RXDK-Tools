using System.ComponentModel;
using Rxdk.XbShellExt.Ui;

namespace Rxdk.XbShellExt.Ui.Forms;

/// <summary>Thin WinForms base for shell dialogs. No runtime chrome here so the VS designer can load this type reliably.</summary>
public class ShellDialogForm : Form
{
    protected void ApplyRuntimeChrome()
    {
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            ShellModernChrome.Apply(this);
    }
}
