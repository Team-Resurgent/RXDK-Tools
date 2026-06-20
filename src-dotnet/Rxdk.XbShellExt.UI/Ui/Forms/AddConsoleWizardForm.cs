using Rxdk.XbShellExt.Ui;
using Rxdk.XbShellExt.Ui.Controls;
using Rxdk.XbShellExt.Ui.Controls.Wizard;
using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.KitServices.Stores;

namespace Rxdk.XbShellExt.Ui.Forms;

public sealed partial class AddConsoleWizardForm : ShellDialogForm
{
    private enum WizardStep
    {
        Welcome,
        GetName,
        AccessDenied,
        MakeDefault,
        Finish,
    }

    private readonly AddConsoleService _service = new();
    private readonly ShellExtensionConsoleStore _store = new();
    private readonly AddConsoleWizardState _state = new() { MakeDefault = true };
    private WizardStep _step;

    private readonly AddConsoleWelcomePage _welcomePage = new();
    private readonly AddConsoleGetNamePage _getNamePage = new();
    private readonly AddConsoleAccessDeniedPage _accessDeniedPage = new();
    private readonly AddConsoleMakeDefaultPage _makeDefaultPage = new();
    private readonly AddConsoleFinishPage _finishPage = new();

    public AddConsoleWizardForm()
    {
        InitializeComponent();
        ApplyRuntimeChrome();
        Font = ShellWizardChrome.CreateWizardBodyFont();

        backButton.Click += (_, _) => GoBack();
        nextButton.Click += (_, _) => OnNext();
        cancelButton.Click += (_, _) => Close();

        _getNamePage.NameTextBox.TextChanged += (_, _) => UpdateWizardButtons();
        _accessDeniedPage.PasswordTextBox.TextChanged += (_, _) => UpdateWizardButtons();
        foreach (var box in new[]
                 {
                     _accessDeniedPage.PrivReadCheck,
                     _accessDeniedPage.PrivWriteCheck,
                     _accessDeniedPage.PrivConfigureCheck,
                     _accessDeniedPage.PrivControlCheck,
                     _accessDeniedPage.PrivManageCheck,
                 })
        {
            box.CheckedChanged += (_, _) => UpdateWizardButtons();
        }

        _makeDefaultPage.MakeDefaultYes.CheckedChanged += (_, _) => UpdateWizardButtons();
        _makeDefaultPage.MakeDefaultNo.CheckedChanged += (_, _) => UpdateWizardButtons();

        if (DesignPreview.IsDesignTime(this))
        {
            ApplyDesignPreview();
            return;
        }

        ShowStep(WizardStep.Welcome);
    }

    private void ApplyDesignPreview()
    {
        chrome.ShowInnerPageLayout(
            "Specifying an Xbox Development Kit",
            "The wizard needs to know which Xbox Development Kit to add.");
        _getNamePage.SetConsoleName(DesignPreview.SampleConsoleName);
        chrome.ContentHost.Controls.Add(_getNamePage);
        backButton.Enabled = true;
        nextButton.Enabled = true;
    }

    private void ShowStep(WizardStep step)
    {
        _step = step;
        statusLabel.Text = string.Empty;
        statusLabel.Padding = new Padding(
            step is WizardStep.Welcome or WizardStep.Finish ? ShellWizardChrome.BannerWidth + 12 : 12,
            0,
            12,
            4);
        chrome.ContentHost.Controls.Clear();

        switch (step)
        {
        case WizardStep.Welcome:
            chrome.ShowWelcomeOrFinishLayout();
            chrome.ContentHost.Controls.Add(_welcomePage);
            break;
        case WizardStep.GetName:
            chrome.ShowInnerPageLayout(
                "Specifying an Xbox Development Kit",
                "The wizard needs to know which Xbox Development Kit to add.");
            _getNamePage.SetConsoleName(_state.ConsoleName);
            chrome.ContentHost.Controls.Add(_getNamePage);
            break;
        case WizardStep.AccessDenied:
            chrome.ShowInnerPageLayout(
                "This machine does not have access to the specified Xbox Development Kit.",
                "If you know the Administrator password, you may give this machine access now.");
            _accessDeniedPage.SetDesiredAccess(_state.DesiredAccess);
            chrome.ContentHost.Controls.Add(_accessDeniedPage);
            break;
        case WizardStep.MakeDefault:
            chrome.ShowInnerPageLayout(
                "Choosing this Xbox as default.",
                "The default Xbox is used by Visual Studio, and other Xbox development tools.");
            _makeDefaultPage.SetPrompt(_state.ConsoleName);
            _makeDefaultPage.SetMakeDefault(_state.MakeDefault);
            chrome.ContentHost.Controls.Add(_makeDefaultPage);
            break;
        case WizardStep.Finish:
            chrome.ShowWelcomeOrFinishLayout();
            PopulateFinishPage();
            chrome.ContentHost.Controls.Add(_finishPage);
            break;
        }

        UpdateWizardButtons();
    }

    private void PopulateFinishPage()
    {
        var ipText = _state.IpAddress.HasValue
            ? $"{_state.ConsoleName}({FormattingHelper.FormatIpAddress(_state.IpAddress.Value)})"
            : _state.ConsoleName;
        _finishPage.ConsoleValueLabel.Text = ipText;
        _finishPage.DefaultValueLabel.Text = _state.MakeDefault ? "Yes" : "No";
        _finishPage.PermissionsValueLabel.Text = FormatPermissions(_state.DesiredAccess);
        _finishPage.ShowPermissionsRow(_state.NeedsSecurityStep);
    }

    private void UpdateWizardButtons()
    {
        backButton.Visible = true;
        nextButton.Visible = true;
        backButton.Enabled = _step != WizardStep.Welcome;
        nextButton.Text = _step == WizardStep.Finish ? "&Finish" : "&Next >";

        nextButton.Enabled = _step switch
        {
            WizardStep.Welcome => true,
            WizardStep.GetName => !string.IsNullOrWhiteSpace(_getNamePage.NameTextBox.Text),
            WizardStep.AccessDenied => CanProceedFromSecurityPage(),
            WizardStep.MakeDefault => _makeDefaultPage.MakeDefaultYes.Checked || _makeDefaultPage.MakeDefaultNo.Checked,
            WizardStep.Finish => true,
            _ => true,
        };
    }

    private bool CanProceedFromSecurityPage()
    {
        if (string.IsNullOrWhiteSpace(_accessDeniedPage.PasswordTextBox.Text))
            return false;

        return ReadDesiredAccess() != 0;
    }

    private static string FormatPermissions(uint access)
    {
        var parts = new List<string>();
        if ((access & XbdmConstants.PrivRead) != 0) parts.Add("Read");
        if ((access & XbdmConstants.PrivWrite) != 0) parts.Add("Write");
        if ((access & XbdmConstants.PrivConfigure) != 0) parts.Add("Configure");
        if ((access & XbdmConstants.PrivControl) != 0) parts.Add("Control");
        if ((access & XbdmConstants.PrivManage) != 0) parts.Add("Manage");
        return string.Join(", ", parts);
    }

    private void GoBack()
    {
        switch (_step)
        {
        case WizardStep.GetName:
            ShowStep(WizardStep.Welcome);
            break;
        case WizardStep.AccessDenied:
            ShowStep(WizardStep.GetName);
            break;
        case WizardStep.MakeDefault:
            ShowStep(_state.NeedsSecurityStep ? WizardStep.AccessDenied : WizardStep.GetName);
            break;
        case WizardStep.Finish:
            ShowStep(WizardStep.MakeDefault);
            break;
        }
    }

    private void OnNext()
    {
        try
        {
            switch (_step)
            {
            case WizardStep.Welcome:
                ShowStep(WizardStep.GetName);
                return;

            case WizardStep.GetName:
                _state.ConsoleName = _getNamePage.NameTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(_state.ConsoleName))
                {
                    statusLabel.Text = "Enter a console name or IP address.";
                    UpdateWizardButtons();
                    return;
                }

                var probed = _service.ProbeConsole(_state.ConsoleName);
                _state.ConsoleIsValid = probed.ConsoleIsValid;
                _state.IpAddress = probed.IpAddress;
                _state.IsSecurityEnabled = probed.IsSecurityEnabled;
                _state.CurrentAccess = probed.CurrentAccess;
                _state.DesiredAccess = probed.DesiredAccess;
                _state.WireName = probed.WireName;

                if (!_state.ConsoleIsValid || !_state.IpAddress.HasValue)
                {
                    MessageBox.Show(
                        this,
                        $"Could not find Xbox console '{_state.ConsoleName}'.",
                        "Xbox Neighborhood",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Stop);
                    return;
                }

                ShowStep(_state.NeedsSecurityStep ? WizardStep.AccessDenied : WizardStep.MakeDefault);
                return;

            case WizardStep.AccessDenied:
                _state.Password = _accessDeniedPage.PasswordTextBox.Text ?? "";
                _state.DesiredAccess = ReadDesiredAccess();
                _service.ValidatePassword(_state);
                ShowStep(WizardStep.MakeDefault);
                return;

            case WizardStep.MakeDefault:
                _state.MakeDefault = _makeDefaultPage.MakeDefaultYes.Checked;
                ShowStep(WizardStep.Finish);
                return;

            case WizardStep.Finish:
                _service.Finish(_state, _store);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
    }

    private uint ReadDesiredAccess()
    {
        uint access = 0;
        if (_accessDeniedPage.PrivReadCheck.Checked) access |= XbdmConstants.PrivRead;
        if (_accessDeniedPage.PrivWriteCheck.Checked) access |= XbdmConstants.PrivWrite;
        if (_accessDeniedPage.PrivConfigureCheck.Checked) access |= XbdmConstants.PrivConfigure;
        if (_accessDeniedPage.PrivControlCheck.Checked) access |= XbdmConstants.PrivControl;
        if (_accessDeniedPage.PrivManageCheck.Checked) access |= XbdmConstants.PrivManage;
        return access;
    }
}
