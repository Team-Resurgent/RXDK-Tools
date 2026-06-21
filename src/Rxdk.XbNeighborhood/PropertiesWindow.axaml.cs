using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Rxdk.XbNeighborhood.Core.Models;
using Rxdk.XbNeighborhood.Core.Services;
using Rxdk.XbNeighborhood.Services;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;
using KitPropertyModels = Rxdk.Xbdm.KitServices.Models;

namespace Rxdk.XbNeighborhood;

public partial class PropertiesWindow : Window
{
    private readonly PropertySession _session;
    private readonly PropertiesService _propertiesService = new();
    private readonly SecurityService _securityService = new();
    private readonly Action? _onApplied;

    private CheckBox? _readOnlyCheck;
    private CheckBox? _hiddenCheck;
    private ListBox? _securityUsers;
    private CheckBox? _privRead;
    private CheckBox? _privWrite;
    private CheckBox? _privConfigure;
    private CheckBox? _privControl;
    private CheckBox? _privManage;
    private TextBox? _securePassword;
    private StackPanel? _securityManagePanel;
    private StackPanel? _securityUnlockPanel;
    private StackPanel? _securityNoManagePanel;
    private StackPanel? _securityReadOnlyPanel;

    public PropertiesWindow()
    {
        InitializeComponent();
        _session = null!;
    }

    public PropertiesWindow(PropertySession session, Action? onApplied = null, string? initialTabHeader = null)
    {
        _session = session;
        _onApplied = onApplied;
        InitializeComponent();
        Title = $"{session.Context.Caption} Properties";
        BuildTabs();
        if (!string.IsNullOrWhiteSpace(initialTabHeader))
            SelectTab(initialTabHeader);
    }

    private void SelectTab(string header)
    {
        var tabs = this.FindControl<TabControl>("PropertyTabs");
        if (tabs == null)
            return;

        foreach (var item in tabs.Items)
        {
            if (item is TabItem tab && string.Equals(tab.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
            {
                tabs.SelectedItem = tab;
                break;
            }
        }
    }

    private void BuildTabs()
    {
        var tabs = this.FindControl<TabControl>("PropertyTabs")!;

        switch (_session.Context.Kind)
        {
            case KitPropertyModels.PropertyTargetKind.Console:
                tabs.Items.Add(CreateConsoleGeneralTab());
                tabs.Items.Add(CreateConsoleAdvancedTab());
                tabs.Items.Add(CreateSecurityTab());
                break;
            case KitPropertyModels.PropertyTargetKind.Drive:
                tabs.Items.Add(CreateDriveTab());
                break;
            default:
                tabs.Items.Add(CreateFileTab());
                break;
        }
    }

    private TabItem CreateConsoleGeneralTab()
    {
        var info = _session.Context.ConsoleGeneral!;
        var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(16) };
        panel.Children.Add(CreateIconHeader(info.Name, GetConsolePropertyIcon()));
        panel.Children.Add(PropRow("IP address", info.IpAddress ?? "Not available"));
        if (!string.IsNullOrWhiteSpace(info.AltIpAddress))
            panel.Children.Add(PropRow("Alternate IP", info.AltIpAddress));
        panel.Children.Add(PropRow("Running title", info.RunningTitle ?? "Not available"));
        return new TabItem { Header = "General", Content = WrapTabContent(panel) };
    }

    private TabItem CreateConsoleAdvancedTab()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(16) };
        var warm = new CheckBox { Content = "Warm reboot", IsChecked = true };
        var sameTitle = new CheckBox { Content = "Restart same title" };
        var reboot = new Button { Content = "Reboot" };
        reboot.Click += (_, _) =>
        {
            try
            {
                string? launch = null;
                if (sameTitle.IsChecked == true)
                    launch = _session.Connection.GetXbeLaunchPath();
                _session.Connection.Reboot(cold: warm.IsChecked != true, launch);
            }
            catch (XbdmException ex)
            {
                ShowError(ex.Message);
            }
        };

        var screenshot = new Button { Content = "Capture screenshot..." };
        screenshot.Click += async (_, _) => await CaptureScreenshotAsync();

        panel.Children.Add(warm);
        panel.Children.Add(sameTitle);
        panel.Children.Add(reboot);
        panel.Children.Add(screenshot);
        return new TabItem { Header = "Advanced", Content = WrapTabContent(panel) };
    }

    private TabItem CreateDriveTab()
    {
        var drive = _session.Context.Drive!;
        var panel = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(16) };
        panel.Children.Add(CreateIconHeader(drive.Description, ShellIconService.GetDriveIcon()));
        panel.Children.Add(PropRow("Type", drive.DriveType));

        var pieRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16 };
        pieRow.Children.Add(new Controls.DriveUsagePie { UsedPercent = drive.UsedPercent });
        var legend = new StackPanel { Spacing = 6, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        legend.Children.Add(LegendRow(UsedBrushColor(), "Used space"));
        legend.Children.Add(LegendRow(FreeBrushColor(), "Free space"));
        pieRow.Children.Add(legend);
        panel.Children.Add(pieRow);

        panel.Children.Add(PropRow("Used", $"{FormattingHelper.FormatFileSizeBytes(drive.UsedBytes)} ({FormattingHelper.FormatFileSize(drive.UsedBytes)})"));
        panel.Children.Add(PropRow("Free", $"{FormattingHelper.FormatFileSizeBytes(drive.FreeBytes)} ({FormattingHelper.FormatFileSize(drive.FreeBytes)})"));
        panel.Children.Add(PropRow("Capacity", $"{FormattingHelper.FormatFileSizeBytes(drive.TotalBytes)} ({FormattingHelper.FormatFileSize(drive.TotalBytes)})"));
        return new TabItem { Header = "General", Content = WrapTabContent(panel) };
    }

    private TabItem CreateFileTab()
    {
        var file = _session.Context.File!;
        var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(16) };

        panel.Children.Add(CreateIconHeader(file.DisplayName ?? _session.Context.Caption, GetFilePropertyIcon(file)));
        if (!string.IsNullOrWhiteSpace(file.TypeName))
            panel.Children.Add(PropRow("Type", file.TypeName));
        if (!string.IsNullOrWhiteSpace(file.Location))
            panel.Children.Add(PropRow("Location", file.Location));
        if (file.TotalSize > 0 || _session.Context.Kind == KitPropertyModels.PropertyTargetKind.File)
            panel.Children.Add(PropRow("Size", FormattingHelper.FormatFileSize(file.TotalSize)));
        if (_session.Context.Kind is KitPropertyModels.PropertyTargetKind.Folder or KitPropertyModels.PropertyTargetKind.MultiFile)
            panel.Children.Add(PropRow("Contains", $"{file.FileCount} file(s), {file.FolderCount} folder(s)"));
        if (file.Created.HasValue)
            panel.Children.Add(PropRow("Created", FormattingHelper.FormatDateTime(file.Created)));
        if (file.Modified.HasValue)
            panel.Children.Add(PropRow("Modified", FormattingHelper.FormatDateTime(file.Modified)));

        var hasAttributeControls = false;
        if (file.ReadOnly.HasValue)
        {
            _readOnlyCheck = new CheckBox { Content = "Read-only", IsChecked = file.ReadOnly };
            panel.Children.Add(_readOnlyCheck);
            hasAttributeControls = true;
        }
        else
        {
            _readOnlyCheck = new CheckBox { Content = "Read-only", IsChecked = null, IsThreeState = true };
            panel.Children.Add(_readOnlyCheck);
            hasAttributeControls = true;
        }

        if (file.Hidden.HasValue)
        {
            _hiddenCheck = new CheckBox { Content = "Hidden", IsChecked = file.Hidden };
            panel.Children.Add(_hiddenCheck);
            hasAttributeControls = true;
        }
        else
        {
            _hiddenCheck = new CheckBox { Content = "Hidden", IsChecked = null, IsThreeState = true };
            panel.Children.Add(_hiddenCheck);
            hasAttributeControls = true;
        }

        if (hasAttributeControls)
        {
            var apply = new Button { Content = "Apply", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
            apply.Click += (_, _) => ApplyFileAttributes(file);
            panel.Children.Add(apply);
        }

        return new TabItem { Header = "General", Content = WrapTabContent(panel) };
    }

    private TabItem CreateSecurityTab()
    {
        var root = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(16) };

        _securityUnlockPanel = new StackPanel { Spacing = 8 };
        _securityUnlockPanel.Children.Add(new TextBlock { Text = "Console security is disabled." });
        var lockBtn = new Button { Content = "Lock console..." };
        lockBtn.Click += async (_, _) => await LockConsoleAsync();
        _securityUnlockPanel.Children.Add(lockBtn);

        _securityNoManagePanel = new StackPanel { Spacing = 8, IsVisible = false };
        _securityNoManagePanel.Children.Add(new TextBlock
        {
            Text = "Enter the administrator password and click Manage to edit users or unlock the console.",
        });
        _securePassword = new TextBox { PasswordChar = '•', PlaceholderText = "Password" };
        _securityNoManagePanel.Children.Add(_securePassword);
        var manageBtn = new Button { Content = "Manage..." };
        manageBtn.Click += (_, _) => StartSecureMode();
        _securityNoManagePanel.Children.Add(manageBtn);
        _securityReadOnlyPanel = new StackPanel { Spacing = 4 };
        _securityNoManagePanel.Children.Add(_securityReadOnlyPanel);

        _securityManagePanel = new StackPanel { Spacing = 8, IsVisible = false };
        _securityUsers = new ListBox { Height = 120 };
        _securityUsers.SelectionChanged += (_, _) => UpdateSecurityPrivilegesUi();
        _securityManagePanel.Children.Add(new TextBlock { Text = "Machines with access:" });
        _securityManagePanel.Children.Add(_securityUsers);

        var userButtons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        var addUser = new Button { Content = "Add..." };
        addUser.Click += async (_, _) => await AddSecurityUserAsync();
        var removeUser = new Button { Content = "Remove" };
        removeUser.Click += (_, _) => RemoveSecurityUser();
        userButtons.Children.Add(addUser);
        userButtons.Children.Add(removeUser);
        _securityManagePanel.Children.Add(userButtons);

        _securityManagePanel.Children.Add(new TextBlock { Text = "Permissions:" });
        _privRead = new CheckBox { Content = "Read" };
        _privWrite = new CheckBox { Content = "Write" };
        _privConfigure = new CheckBox { Content = "Configure" };
        _privControl = new CheckBox { Content = "Control" };
        _privManage = new CheckBox { Content = "Manage" };
        foreach (var box in new[] { _privRead, _privWrite, _privConfigure, _privControl, _privManage })
        {
            box.IsCheckedChanged += (_, _) => OnPrivilegeChanged();
            _securityManagePanel.Children.Add(box);
        }

        var unlockBtn = new Button { Content = "Unlock console..." };
        unlockBtn.Click += async (_, _) => await UnlockConsoleAsync();
        var changePwd = new Button { Content = "Change password..." };
        changePwd.Click += async (_, _) => await ChangePasswordAsync();
        var applyBtn = new Button { Content = "Apply" };
        applyBtn.Click += (_, _) => ApplySecurity();

        _securityManagePanel.Children.Add(unlockBtn);
        _securityManagePanel.Children.Add(changePwd);
        _securityManagePanel.Children.Add(applyBtn);

        root.Children.Add(_securityUnlockPanel);
        root.Children.Add(_securityNoManagePanel);
        root.Children.Add(_securityManagePanel);

        RefreshSecurityUi();
        return new TabItem { Header = "Security", Content = WrapTabContent(root) };
    }

    private static Control WrapTabContent(Control content) =>
        new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = content,
        };

    private static StackPanel LegendRow(Color color, string label)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            Background = new SolidColorBrush(color),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Avalonia.Thickness(1),
        });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        return row;
    }

    private static Color UsedBrushColor() => Color.FromRgb(0x4A, 0x6C, 0xF7);
    private static Color FreeBrushColor() => Color.FromRgb(0x9B, 0x59, 0xB6);

    private void RefreshSecurityUi()
    {
        var state = _session.Security;
        _securityUnlockPanel!.IsVisible = !state.IsLocked;
        _securityNoManagePanel!.IsVisible = state.IsLocked && !state.ManageMode;
        _securityManagePanel!.IsVisible = state.IsLocked && state.ManageMode;

        if (state.IsLocked && state.ManageMode)
        {
            _securityUsers!.ItemsSource = state.Users.Where(u => !u.MarkedForRemove).Select(u => u.UserName).ToList();
            if (!string.IsNullOrWhiteSpace(state.SelectedUserName))
                _securityUsers.SelectedItem = state.SelectedUserName;
            UpdateSecurityPrivilegesUi();
        }
        else if (state.IsLocked && !state.ManageMode)
        {
            _securityReadOnlyPanel!.Children.Clear();
            _securityReadOnlyPanel.Children.Add(new TextBlock { Text = $"This computer: {SecurityService.GetComputerName()}" });
            _securityReadOnlyPanel.Children.Add(new TextBlock { Text = DescribeAccess(state.CurrentAccess) });
        }
    }

    private void UpdateSecurityPrivilegesUi()
    {
        if (_securityUsers?.SelectedItem is not string userName)
            return;

        var user = _session.Security.Users.FirstOrDefault(u => u.UserName == userName);
        if (user == null)
            return;

        _session.Security.SelectedUserName = userName;
        SetPrivilegeChecks(user.NewAccess, enabled: true);
    }

    private void OnPrivilegeChanged()
    {
        if (_securityUsers?.SelectedItem is not string userName)
            return;

        var user = _session.Security.Users.FirstOrDefault(u => u.UserName == userName);
        if (user == null)
            return;

        uint access = 0;
        if (_privRead?.IsChecked == true) access |= XbdmConstants.PrivRead;
        if (_privWrite?.IsChecked == true) access |= XbdmConstants.PrivWrite;
        if (_privConfigure?.IsChecked == true) access |= XbdmConstants.PrivConfigure;
        if (_privControl?.IsChecked == true) access |= XbdmConstants.PrivControl;
        if (_privManage?.IsChecked == true) access |= XbdmConstants.PrivManage;
        user.NewAccess = access;
    }

    private void SetPrivilegeChecks(uint access, bool enabled)
    {
        foreach (var box in new[] { _privRead, _privWrite, _privConfigure, _privControl, _privManage })
            box!.IsEnabled = enabled;

        _privRead!.IsChecked = (access & XbdmConstants.PrivRead) != 0;
        _privWrite!.IsChecked = (access & XbdmConstants.PrivWrite) != 0;
        _privConfigure!.IsChecked = (access & XbdmConstants.PrivConfigure) != 0;
        _privControl!.IsChecked = (access & XbdmConstants.PrivControl) != 0;
        _privManage!.IsChecked = (access & XbdmConstants.PrivManage) != 0;
    }

    private static string DescribeAccess(uint access)
    {
        var parts = new List<string>();
        if ((access & XbdmConstants.PrivRead) != 0) parts.Add("Read");
        if ((access & XbdmConstants.PrivWrite) != 0) parts.Add("Write");
        if ((access & XbdmConstants.PrivConfigure) != 0) parts.Add("Configure");
        if ((access & XbdmConstants.PrivControl) != 0) parts.Add("Control");
        if ((access & XbdmConstants.PrivManage) != 0) parts.Add("Manage");
        return parts.Count == 0 ? "No permissions" : string.Join(", ", parts);
    }

    private async Task LockConsoleAsync()
    {
        if (!await ConfirmAsync("Lock Console", $"Lock security on '{_session.Context.ConsoleName}'?"))
            return;

        try
        {
            _securityService.LockConsole(_session.Connection, _session.Security, SecurityService.GetComputerName());
            RefreshSecurityUi();
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task UnlockConsoleAsync()
    {
        if (!await ConfirmAsync("Unlock Console", $"Disable security on '{_session.Context.ConsoleName}'?"))
            return;

        try
        {
            _securityService.UnlockConsole(_session.Connection, _session.Security);
            RefreshSecurityUi();
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
    }

    private void StartSecureMode()
    {
        try
        {
            _securityService.StartSecureMode(_session.Connection, _session.Security, _securePassword?.Text ?? "");
            RefreshSecurityUi();
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task AddSecurityUserAsync()
    {
        var name = await PromptTextAsync("Add User", "Machine name:");
        if (string.IsNullOrWhiteSpace(name))
            return;

        _securityService.AddUser(_session.Security, name.Trim());
        RefreshSecurityUi();
    }

    private void RemoveSecurityUser()
    {
        if (_securityUsers?.SelectedItem is not string userName)
            return;
        _securityService.RemoveUser(_session.Security, userName);
        RefreshSecurityUi();
    }

    private void ApplySecurity()
    {
        try
        {
            _securityService.ApplyChanges(_session.Connection, _session.Security);
            ShowInfo("Security changes applied.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task ChangePasswordAsync()
    {
        var password = await PromptTextAsync("Change Password", "New administrator password:", password: true);
        if (string.IsNullOrEmpty(password))
            return;

        var confirm = await PromptTextAsync("Change Password", "Confirm password:", password: true);
        if (password != confirm)
        {
            ShowError("Passwords do not match.");
            return;
        }

        try
        {
            _securityService.SetAdminPassword(_session.Connection, password);
            ShowInfo("Password updated.");
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ApplyFileAttributes(KitPropertyModels.FileGeneralInfo file)
    {
        if (_readOnlyCheck != null)
            file.ReadOnly = _readOnlyCheck.IsChecked;
        if (_hiddenCheck != null)
            file.Hidden = _hiddenCheck.IsChecked;

        try
        {
            _propertiesService.ApplyFileAttributes(_session.Connection, file);
            _onApplied?.Invoke();
            ShowInfo("Attributes updated.");
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task CaptureScreenshotAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save screenshot",
            DefaultExtension = "bmp",
            SuggestedFileName = "screenshot.bmp",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Bitmap") { Patterns = new[] { "*.bmp" } },
            },
        });

        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        try
        {
            _session.Connection.CaptureScreenshot(path);
            ShowInfo("Screenshot saved.");
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
    }

    private static StackPanel CreateIconHeader(string title, IImage? icon)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 8) };
        if (icon != null)
            row.Children.Add(new Image { Source = icon, Width = 32, Height = 32 });
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        return row;
    }

    private IImage? GetConsolePropertyIcon()
    {
        var registry = new ConsoleRegistryService();
        var isDefault = string.Equals(
            registry.GetDefaultConsoleName(),
            _session.Context.ConsoleName,
            StringComparison.OrdinalIgnoreCase);
        return ShellIconService.GetConsoleIcon(isDefault);
    }

    private IImage? GetFilePropertyIcon(KitPropertyModels.FileGeneralInfo file)
    {
        if (_session.Context.Kind == KitPropertyModels.PropertyTargetKind.MultiFile)
            return ShellIconService.GetMultiItemIcon();

        if (file.Items.Count == 1)
            return ShellIconService.GetItemIcon(file.Items[0].Name, file.Items[0].IsDirectory);

        return ShellIconService.GetMultiItemIcon();
    }

    private static StackPanel PropRow(string label, string value)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Opacity = 0.75 });
        row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
        return row;
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var yes = false;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var yesBtn = new Button { Content = "Yes", IsDefault = true };
        var noBtn = new Button { Content = "No", IsCancel = true };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(yesBtn);
        buttons.Children.Add(noBtn);
        panel.Children.Add(buttons);
        var dialog = new Window { Title = title, Width = 420, Height = 160, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        yesBtn.Click += (_, _) => { yes = true; dialog.Close(); };
        noBtn.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return yes;
    }

    private async Task<string?> PromptTextAsync(string title, string label, bool password = false)
    {
        var box = password ? new TextBox { PasswordChar = '•' } : new TextBox();
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box);
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        string? result = null;
        var dialog = new Window { Title = title, Width = 400, Height = 170, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => { result = box.Text?.Trim(); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return result;
    }

    private void ShowError(string message) => StatusText.Text = message;
    private void ShowInfo(string message) => StatusText.Text = message;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _session.Dispose();
        base.OnClosed(e);
    }
}
