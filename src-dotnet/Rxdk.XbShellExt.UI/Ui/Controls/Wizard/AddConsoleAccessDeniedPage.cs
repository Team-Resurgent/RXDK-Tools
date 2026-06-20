using Rxdk.Xbdm;
using Rxdk.XbShellExt.Ui;

namespace Rxdk.XbShellExt.Ui.Controls.Wizard;

public sealed partial class AddConsoleAccessDeniedPage : UserControl
{
    public TextBox PasswordTextBox => passwordTextBox;
    public CheckBox PrivReadCheck => privReadCheck;
    public CheckBox PrivWriteCheck => privWriteCheck;
    public CheckBox PrivConfigureCheck => privConfigureCheck;
    public CheckBox PrivControlCheck => privControlCheck;
    public CheckBox PrivManageCheck => privManageCheck;

    public AddConsoleAccessDeniedPage()
    {
        InitializeComponent();
        DesignPreview.ApplyIfDesignTime(() => SetDesiredAccess(DesignPreview.SampleDesiredAccess));
    }

    public void SetDesiredAccess(uint access)
    {
        privReadCheck.Checked = (access & XbdmConstants.PrivRead) != 0;
        privWriteCheck.Checked = (access & XbdmConstants.PrivWrite) != 0;
        privConfigureCheck.Checked = (access & XbdmConstants.PrivConfigure) != 0;
        privControlCheck.Checked = (access & XbdmConstants.PrivControl) != 0;
        privManageCheck.Checked = (access & XbdmConstants.PrivManage) != 0;
    }
}
