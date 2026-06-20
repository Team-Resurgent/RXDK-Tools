using RXDKNeighborhood.Core.Services;
using Rxdk.XbShellExt.Ui;
using Rxdk.XbShellExt.Ui.Controls.Properties;
using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;
using KitPropertiesService = Rxdk.Xbdm.KitServices.Services.PropertiesService;
using KitPropertySession = Rxdk.Xbdm.KitServices.Services.PropertySession;
using KitFileOperationsService = Rxdk.Xbdm.KitServices.Services.FileOperationsService;
using KitFileClipboardService = Rxdk.Xbdm.KitServices.Services.FileClipboardService;

namespace Rxdk.XbShellExt.Ui.Forms;

public sealed partial class PropertiesForm : ShellDialogForm
{
    private readonly KitPropertySession _session = null!;
    private readonly KitPropertiesService _propertiesService = new();
    private readonly KitFileOperationsService _fileOperations = new(new KitFileClipboardService());
    private readonly string _folderDisplayPath = null!;

    private string _baselineName = string.Empty;
    private CheckState _baselineReadOnly = CheckState.Indeterminate;
    private CheckState _baselineHidden = CheckState.Indeterminate;
    private bool _supportsRename;
    private bool _supportsAttributes;

    private FileGeneralTab? _fileGeneralTab;

    public PropertiesForm()
    {
        InitializeComponent();
        ApplyRuntimeChrome();
    }

    public PropertiesForm(RXDKNeighborhood.Core.Services.PropertyRequest request, string? initialTab)
        : this()
    {
        tabs.TabPages.Clear();

        _folderDisplayPath = request.FolderDisplayPath;
        _session = new KitPropertiesService().OpenProperties(request);
        Text = $"{_session.Context.Caption} Properties";

        okButton.Click += (_, _) => OnOk();
        applyButton.Click += (_, _) => ApplyPendingChanges(closeAfterApply: false);
        cancelButton.Click += (_, _) => Close();

        BuildTabs(initialTab);
        ClientSize = GetClientSizeForKind(_session.Context.Kind);
        FormClosed += (_, _) => _session.Dispose();
    }

    private static Size GetClientSizeForKind(PropertyTargetKind kind) => kind switch
    {
        PropertyTargetKind.Drive => new Size(440, 430),
        PropertyTargetKind.Console => new Size(440, 380),
        PropertyTargetKind.File => new Size(440, 490),
        PropertyTargetKind.Folder => new Size(440, 510),
        PropertyTargetKind.MultiFile => new Size(440, 470),
        _ => new Size(440, 490),
    };

    private void BuildTabs(string? initialTab)
    {
        switch (_session.Context.Kind)
        {
            case PropertyTargetKind.Console:
                var consoleGeneral = new ConsoleGeneralTab();
                var info = _session.Context.ConsoleGeneral!;
                consoleGeneral.Bind(
                    info.Name,
                    info.IpAddress ?? "Not available",
                    info.AltIpAddress,
                    info.RunningTitle ?? "Not available");
                tabs.TabPages.Add(CreatePage("General", consoleGeneral));

                var consoleAdvanced = new ConsoleAdvancedTab();
                consoleAdvanced.WireReboot(_session);
                tabs.TabPages.Add(CreatePage("Advanced", consoleAdvanced));

                var security = new SecurityTab();
                security.Bind(_session.Security.IsLocked, FormatAccess(_session.Security.CurrentAccess));
                tabs.TabPages.Add(CreatePage("Security", security));
                break;

            case PropertyTargetKind.Drive:
                var driveGeneral = new DriveGeneralTab();
                driveGeneral.Bind(_session.Context.Drive!);
                tabs.TabPages.Add(CreatePage("General", driveGeneral));
                break;

            default:
                _fileGeneralTab = new FileGeneralTab();
                _supportsRename = _session.Context.Kind is PropertyTargetKind.File or PropertyTargetKind.Folder;
                _supportsAttributes = true;
                _fileGeneralTab.Bind(_session.Context.File!, _session.Context.Kind, _session.Context.Caption);
                tabs.TabPages.Add(CreatePage("General", _fileGeneralTab));
                CaptureFileBaselines();
                break;
        }

        if (!string.IsNullOrWhiteSpace(initialTab))
        {
            foreach (TabPage page in tabs.TabPages)
            {
                if (string.Equals(page.Text, initialTab, StringComparison.OrdinalIgnoreCase))
                {
                    tabs.SelectedTab = page;
                    break;
                }
            }
        }
    }

    private static TabPage CreatePage(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(12) };
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = false };
        content.Dock = DockStyle.Top;
        content.AutoSize = true;
        host.Controls.Add(content);
        page.Controls.Add(host);
        return page;
    }

    private void CaptureFileBaselines()
    {
        var nameTextBox = _fileGeneralTab?.NameTextBox;
        if (nameTextBox != null)
        {
            _baselineName = nameTextBox.Text;
            nameTextBox.TextChanged += (_, _) => UpdateApplyState();
        }

        var readOnlyCheck = _fileGeneralTab?.ReadOnlyCheck;
        if (readOnlyCheck != null)
        {
            _baselineReadOnly = readOnlyCheck.CheckState;
            readOnlyCheck.CheckStateChanged += (_, _) => UpdateApplyState();
        }

        var hiddenCheck = _fileGeneralTab?.HiddenCheck;
        if (hiddenCheck != null)
        {
            _baselineHidden = hiddenCheck.CheckState;
            hiddenCheck.CheckStateChanged += (_, _) => UpdateApplyState();
        }

        UpdateApplyState();
    }

    private void UpdateApplyState()
    {
        if (!_supportsAttributes && !_supportsRename)
        {
            applyButton.Enabled = false;
            return;
        }

        applyButton.Enabled = HasAttributeChanges();
    }

    private bool HasAttributeChanges() =>
        _supportsAttributes &&
        ((_fileGeneralTab?.ReadOnlyCheck != null && _fileGeneralTab.ReadOnlyCheck.CheckState != _baselineReadOnly) ||
         (_fileGeneralTab?.HiddenCheck != null && _fileGeneralTab.HiddenCheck.CheckState != _baselineHidden));

    private bool HasRenameChange() =>
        _supportsRename &&
        _fileGeneralTab?.NameTextBox != null &&
        !string.Equals(_fileGeneralTab.NameTextBox.Text.Trim(), _baselineName.Trim(), StringComparison.OrdinalIgnoreCase);

    private void OnOk()
    {
        if (HasAttributeChanges() || HasRenameChange())
        {
            if (!ApplyPendingChanges(closeAfterApply: true))
                return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private bool ApplyPendingChanges(bool closeAfterApply)
    {
        var file = _session.Context.File;
        if (file == null)
            return true;

        try
        {
            if (HasAttributeChanges())
            {
                if (!TryApplyAttributes(file))
                    return false;

                if (_fileGeneralTab?.ReadOnlyCheck != null)
                    _baselineReadOnly = _fileGeneralTab.ReadOnlyCheck.CheckState;
                if (_fileGeneralTab?.HiddenCheck != null)
                    _baselineHidden = _fileGeneralTab.HiddenCheck.CheckState;
                UpdateApplyState();
            }

            if (closeAfterApply && HasRenameChange())
            {
                if (!TryApplyRename(file))
                    return false;

                _baselineName = _fileGeneralTab!.NameTextBox!.Text.Trim();
            }

            NotifyFolderRefresh();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Xbox Neighborhood", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private bool TryApplyAttributes(FileGeneralInfo file)
    {
        ApplyAttributeSelections(file);

        var setReadOnly = file.ReadOnly == true;
        var clearReadOnly = file.ReadOnly == false;
        var setHidden = file.Hidden == true;
        var clearHidden = file.Hidden == false;

        if (!setReadOnly && !clearReadOnly && !setHidden && !clearHidden)
            return true;

        var recursive = false;
        if (KitPropertiesService.ShouldConfirmRecursiveAttributes(_session.Context.Kind, file))
        {
            var summary = KitPropertiesService.BuildAttributeChangeSummary(
                setReadOnly,
                clearReadOnly,
                setHidden,
                clearHidden);
            var scope = _session.Context.Kind == PropertyTargetKind.MultiFile
                ? "the selected items"
                : "this folder";

            using var dialog = new ConfirmAttributeChangesForm(
                summary,
                scope,
                _session.Context.Kind == PropertyTargetKind.MultiFile);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return false;

            recursive = dialog.ApplyRecursively;
        }

        _propertiesService.ApplyFileAttributes(_session.Connection, file, recursive);
        return true;
    }

    private bool TryApplyRename(FileGeneralInfo file)
    {
        if (file.Items.Count != 1 || _fileGeneralTab?.NameTextBox == null)
            return true;

        var newName = _fileGeneralTab.NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName) ||
            string.Equals(newName, _baselineName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var selection = new FileSelection
        {
            ConsoleName = _session.Context.ConsoleName,
            FolderDisplayPath = _folderDisplayPath,
            Items = file.Items,
        };
        _fileOperations.Rename(selection, newName);
        _fileGeneralTab.NameTextBox.Text = newName;
        return true;
    }

    private void ApplyAttributeSelections(FileGeneralInfo file)
    {
        var readOnlyCheck = _fileGeneralTab?.ReadOnlyCheck;
        if (readOnlyCheck != null && readOnlyCheck.CheckState != CheckState.Indeterminate)
            file.ReadOnly = readOnlyCheck.Checked;

        var hiddenCheck = _fileGeneralTab?.HiddenCheck;
        if (hiddenCheck != null && hiddenCheck.CheckState != CheckState.Indeterminate)
            file.Hidden = hiddenCheck.Checked;
    }

    private static void NotifyFolderRefresh() => ShellNotify.NotifyFolderContentsChanged();

    private static string FormatAccess(uint access) =>
        access == XbdmConstants.PrivAll ? "Full access" : access.ToString();
}
