using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Rxdk.XbNeighborhood.Core.Models;
using Rxdk.XbNeighborhood.Core.Services;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbNeighborhood;

public sealed class AddConsoleWizardWindow : Window
{
    private readonly AddConsoleService _service = new();
    private readonly ConsoleRegistryService _registry;
    private readonly AddConsoleWizardState _state = new();
    private readonly StackPanel _pageHost;
    private readonly TextBlock _statusText;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private int _step;

    public bool Added { get; private set; }

    public AddConsoleWizardWindow(ConsoleRegistryService registry)
    {
        _registry = registry;
        Title = "Add Xbox Console";
        Width = 520;
        Height = 420;
        MinWidth = 460;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _pageHost = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        _statusText = new TextBlock { Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
        _backButton = new Button { Content = "Back", MinWidth = 80 };
        _nextButton = new Button { Content = "Next", MinWidth = 80, IsDefault = true };
        _backButton.Click += (_, _) => GoBack();
        _nextButton.Click += (_, _) => _ = GoNextAsync();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(_backButton);
        buttons.Children.Add(_nextButton);

        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        var scroll = new ScrollViewer { Content = _pageHost };
        Grid.SetRow(scroll, 0);
        grid.Children.Add(scroll);

        var statusBorder = new Border { Padding = new Thickness(12, 8), Child = _statusText };
        Grid.SetRow(statusBorder, 1);
        grid.Children.Add(statusBorder);

        var buttonBorder = new Border { Padding = new Thickness(12), Child = buttons };
        Grid.SetRow(buttonBorder, 2);
        grid.Children.Add(buttonBorder);

        Content = grid;
        ShowStep(0);
    }

    private void ShowStep(int step)
    {
        _step = step;
        _pageHost.Children.Clear();
        _statusText.Text = "";

        switch (step)
        {
            case 0:
                BuildWelcomePage();
                _backButton.IsVisible = false;
                _nextButton.Content = "Next";
                break;
            case 1:
                BuildNamePage();
                _backButton.IsVisible = true;
                _nextButton.Content = "Next";
                break;
            case 2:
                BuildSecurityPage();
                _backButton.IsVisible = true;
                _nextButton.Content = "Next";
                break;
            case 3:
                BuildDefaultPage();
                _backButton.IsVisible = true;
                _nextButton.Content = "Finish";
                break;
        }
    }

    private void BuildWelcomePage()
    {
        _pageHost.Children.Add(new TextBlock
        {
            Text = "Add Xbox Console Wizard",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
        });
        _pageHost.Children.Add(new TextBlock
        {
            Text = "This wizard helps you add an Xbox development kit to Neighborhood and configure access if security is enabled.",
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private TextBox? _nameBox;

    private void BuildNamePage()
    {
        _pageHost.Children.Add(new TextBlock { Text = "Console name or IP address", FontWeight = FontWeight.SemiBold });
        _nameBox = new TextBox { Text = _state.ConsoleName, PlaceholderText = "Kit name or IP" };
        _pageHost.Children.Add(_nameBox);
        _pageHost.Children.Add(new TextBlock
        {
            Text = "Enter the name or IP address of the Xbox kit on your network, then click Next to verify the connection.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
        });
    }

    private TextBox? _passwordBox;
    private CheckBox? _privRead;
    private CheckBox? _privWrite;
    private CheckBox? _privConfigure;
    private CheckBox? _privControl;
    private CheckBox? _privManage;

    private void BuildSecurityPage()
    {
        _pageHost.Children.Add(new TextBlock
        {
            Text = "Console security is enabled. Enter the administrator password and choose the permissions this computer should have.",
            TextWrapping = TextWrapping.Wrap,
        });
        _passwordBox = new TextBox { PasswordChar = '•', PlaceholderText = "Administrator password" };
        _pageHost.Children.Add(_passwordBox);
        _privRead = new CheckBox { Content = "Read", IsChecked = (_state.DesiredAccess & XbdmConstants.PrivRead) != 0 };
        _privWrite = new CheckBox { Content = "Write", IsChecked = (_state.DesiredAccess & XbdmConstants.PrivWrite) != 0 };
        _privConfigure = new CheckBox { Content = "Configure", IsChecked = (_state.DesiredAccess & XbdmConstants.PrivConfigure) != 0 };
        _privControl = new CheckBox { Content = "Control", IsChecked = (_state.DesiredAccess & XbdmConstants.PrivControl) != 0 };
        _privManage = new CheckBox { Content = "Manage", IsChecked = (_state.DesiredAccess & XbdmConstants.PrivManage) != 0 };
        foreach (var box in new[] { _privRead, _privWrite, _privConfigure, _privControl, _privManage })
            _pageHost.Children.Add(box!);
    }

    private CheckBox? _makeDefaultCheck;

    private void BuildDefaultPage()
    {
        _pageHost.Children.Add(new TextBlock
        {
            Text = $"Ready to add '{_state.ConsoleName}'.",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        });
        if (_state.IpAddress.HasValue)
        {
            _pageHost.Children.Add(new TextBlock
            {
                Text = $"IP address: {FormattingHelper.FormatIpAddress(_state.IpAddress.Value)}",
            });
        }

        _makeDefaultCheck = new CheckBox { Content = "Make this the default console", IsChecked = _state.MakeDefault };
        _pageHost.Children.Add(_makeDefaultCheck);
    }

    private void GoBack()
    {
        if (_step == 2)
            ShowStep(1);
        else if (_step == 3)
            ShowStep(_state.NeedsSecurityStep ? 2 : 1);
        else if (_step == 1)
            ShowStep(0);
    }

    private async Task GoNextAsync()
    {
        try
        {
            if (_step == 0)
            {
                ShowStep(1);
                return;
            }

            if (_step == 1)
            {
                _state.ConsoleName = _nameBox?.Text?.Trim() ?? "";
                var probed = _service.ProbeConsole(_state.ConsoleName);
                _state.ConsoleIsValid = probed.ConsoleIsValid;
                _state.IpAddress = probed.IpAddress;
                _state.IsSecurityEnabled = probed.IsSecurityEnabled;
                _state.CurrentAccess = probed.CurrentAccess;
                _state.DesiredAccess = probed.DesiredAccess;

                if (!_state.ConsoleIsValid || !_state.IpAddress.HasValue)
                {
                    _statusText.Text = $"Could not find Xbox console '{_state.ConsoleName}'.";
                    return;
                }

                ShowStep(_state.NeedsSecurityStep ? 2 : 3);
                return;
            }

            if (_step == 2)
            {
                _state.Password = _passwordBox?.Text ?? "";
                _state.DesiredAccess = ReadDesiredAccess();
                if (string.IsNullOrEmpty(_state.Password))
                {
                    _statusText.Text = "Enter the administrator password.";
                    return;
                }

                _service.ValidatePassword(_state);
                ShowStep(3);
                return;
            }

            if (_step == 3)
            {
                _state.MakeDefault = _makeDefaultCheck?.IsChecked == true;
                _service.Finish(_state, _registry);
                Added = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
        }

        await Task.CompletedTask;
    }

    private uint ReadDesiredAccess()
    {
        uint access = 0;
        if (_privRead?.IsChecked == true) access |= XbdmConstants.PrivRead;
        if (_privWrite?.IsChecked == true) access |= XbdmConstants.PrivWrite;
        if (_privConfigure?.IsChecked == true) access |= XbdmConstants.PrivConfigure;
        if (_privControl?.IsChecked == true) access |= XbdmConstants.PrivControl;
        if (_privManage?.IsChecked == true) access |= XbdmConstants.PrivManage;
        return access;
    }
}
