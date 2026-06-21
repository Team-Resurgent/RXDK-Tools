using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Rxdk.XbNeighborhood.Core.Models;
using Rxdk.XbNeighborhood.Core.Services;
using Rxdk.XbNeighborhood.Services;
using Rxdk.XbNeighborhood.ViewModels;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbNeighborhood;

public partial class MainWindow : Window, IFileOperationHost
{
    private readonly ConsoleRegistryService _consoleRegistry = new();
    private readonly FileClipboardService _clipboard = new();
    private readonly XboxBrowserService _browser;
    private readonly FileOperationsService _fileOps;
    private readonly ObservableCollection<FileRowViewModel> _rows = new();

    private string? _selectedConsole;
    private string? _currentDisplayPath;
    private Point? _dragStartPoint;
    private PointerPressedEventArgs? _dragPressArgs;
    private NavTreeItemViewModel? _navRoot;
    private NavTreeItemViewModel? _selectedTreeItem;
    private bool _syncingTreeSelection;

    public MainWindow()
    {
        _browser = new XboxBrowserService(_consoleRegistry);
        _fileOps = new FileOperationsService(_clipboard);
        InitializeComponent();
        FileGrid.ItemsSource = _rows;
        WireDragDrop();
        NavTree.AddHandler(TreeViewItem.ExpandedEvent, OnNavTreeItemExpanded, RoutingStrategies.Bubble);
        Loaded += (_, _) => RefreshNavigationTree();
    }

    private void WireDragDrop()
    {
        DragDrop.SetAllowDrop(FileDropTarget, true);
        FileDropTarget.AddHandler(DragDrop.DragOverEvent, OnFileGridDragOver);
        FileDropTarget.AddHandler(DragDrop.DropEvent, OnFileGridDrop);
        FileGrid.PointerPressed += OnFileGridPointerPressed;
        FileGrid.PointerMoved += OnFileGridPointerMoved;
    }

    private void RefreshNavigationTree()
    {
        try
        {
            _browser.EnsureNativeInitialized();
            var names = _consoleRegistry.GetConsoleNames();
            if (names.Count == 0)
            {
                var dmDefault = _consoleRegistry.GetDefaultConsoleName();
                if (!string.IsNullOrWhiteSpace(dmDefault))
                {
                    _consoleRegistry.AddConsole(dmDefault);
                    names = _consoleRegistry.GetConsoleNames();
                }
            }

            var defaultConsole = _consoleRegistry.GetDefaultConsoleName();
            _navRoot = new NavTreeItemViewModel
            {
                Title = "Xbox Neighborhood",
                Kind = NavigationNodeKind.Root,
                DisplayPath = "",
                Icon = ShellIconService.GetNeighborhoodIcon(),
            };

            foreach (var name in names)
            {
                var isDefault = string.Equals(name, defaultConsole, StringComparison.OrdinalIgnoreCase);
                var title = isDefault ? $"{name} (default)" : name;
                _navRoot.Children.Add(new NavTreeItemViewModel
                {
                    Title = title,
                    Kind = NavigationNodeKind.Console,
                    DisplayPath = name,
                    ConsoleName = name,
                    Icon = ShellIconService.GetConsoleIcon(isDefault),
                });
            }

            NavTree.ItemsSource = new[] { _navRoot };
            SetStatus($"Loaded {names.Count} console(s).");
        }
        catch (XbdmException ex)
        {
            SetStatus($"Native error: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void OnNavTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingTreeSelection)
            return;

        if (NavTree.SelectedItem is not NavTreeItemViewModel node)
            return;

        _selectedTreeItem = node;
        switch (node.Kind)
        {
            case NavigationNodeKind.Root:
                _selectedConsole = null;
                _currentDisplayPath = null;
                _rows.Clear();
                PathText.Text = "";
                SetStatus("Ready");
                return;
            case NavigationNodeKind.Console:
                _selectedConsole = node.ConsoleName;
                _currentDisplayPath = node.ConsoleName;
                EnsureConsoleDrivesLoaded(node);
                break;
            case NavigationNodeKind.Drive:
            case NavigationNodeKind.Folder:
                _selectedConsole = node.ConsoleName;
                _currentDisplayPath = node.DisplayPath;
                break;
            default:
                return;
        }

        LoadCurrentLocation();
    }

    private void OnNavTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TreeViewItem { DataContext: NavTreeItemViewModel node })
            return;

        if (node.Kind == NavigationNodeKind.Console)
            EnsureConsoleDrivesLoaded(node);
    }

    private void EnsureConsoleDrivesLoaded(NavTreeItemViewModel consoleNode)
    {
        if (consoleNode.ChildrenLoaded || string.IsNullOrWhiteSpace(consoleNode.ConsoleName))
            return;

        try
        {
            foreach (var drive in _browser.LoadDrives(consoleNode.ConsoleName))
            {
                consoleNode.Children.Add(new NavTreeItemViewModel
                {
                    Title = drive.Title,
                    Kind = NavigationNodeKind.Drive,
                    DisplayPath = drive.DisplayPath,
                    ConsoleName = drive.ConsoleName,
                    Icon = ShellIconService.GetDriveIcon(),
                });
            }

            consoleNode.ChildrenLoaded = true;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load drives: {ex.Message}");
        }
    }

    private static string? GetConsoleNameFromTreeItem(NavTreeItemViewModel? item) => item?.Kind switch
    {
        NavigationNodeKind.Console or NavigationNodeKind.Drive => item.ConsoleName,
        _ => null,
    };

    private void LoadCurrentLocation()
    {
        if (string.IsNullOrWhiteSpace(_selectedConsole) || string.IsNullOrWhiteSpace(_currentDisplayPath))
            return;

        try
        {
            _rows.Clear();
            PathText.Text = _currentDisplayPath;

            IReadOnlyList<FileEntryModel> entries;
            if (WirePathService.IsDriveListing(_currentDisplayPath, _selectedConsole))
                entries = _browser.ListDrivesAsEntries(_selectedConsole);
            else
                entries = _browser.ListFolder(_currentDisplayPath, _selectedConsole);

            foreach (var entry in entries.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                _rows.Add(FileRowViewModel.From(entry));

            SetStatus($"{entries.Count} item(s) in {_currentDisplayPath}");
        }
        catch (XbdmException ex)
        {
            SetStatus($"Xbox error: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void OnFileDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (FileGrid.SelectedItem is not FileRowViewModel row || !IsNavigable(row))
            return;

        NavigateInto(row);
    }

    private void NavigateInto(FileRowViewModel row)
    {
        if (string.Equals(row.Type, "Drive", StringComparison.OrdinalIgnoreCase))
            _currentDisplayPath = WirePathService.BuildDriveDisplayPath(_selectedConsole!, row.Name[0]);
        else
            _currentDisplayPath = WirePathService.AppendDisplaySegment(_currentDisplayPath!, row.Name);

        LoadCurrentLocation();
        SyncTreeToCurrentPath();
    }

    private static bool IsNavigable(FileRowViewModel row) =>
        row.IsDirectory || string.Equals(row.Type, "Drive", StringComparison.OrdinalIgnoreCase);

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentDisplayPath) || string.IsNullOrWhiteSpace(_selectedConsole))
            return;

        if (WirePathService.IsDriveListing(_currentDisplayPath, _selectedConsole))
            return;

        var parent = WirePathService.GetParentDisplayPath(_currentDisplayPath);
        _currentDisplayPath = parent ?? _selectedConsole;
        LoadCurrentLocation();
        SyncTreeToCurrentPath();
    }

    private void SyncTreeToCurrentPath()
    {
        if (_navRoot == null || string.IsNullOrWhiteSpace(_selectedConsole) || string.IsNullOrWhiteSpace(_currentDisplayPath))
            return;

        NavTreeItemViewModel? target;
        if (WirePathService.IsDriveListing(_currentDisplayPath, _selectedConsole))
        {
            target = FindConsoleNode(_selectedConsole);
        }
        else
        {
            target = EnsureTreePathExists(_currentDisplayPath, _selectedConsole);
        }

        if (target == null)
            return;

        _syncingTreeSelection = true;
        try
        {
            NavTree.SelectedItem = target;
            _selectedTreeItem = target;
        }
        finally
        {
            _syncingTreeSelection = false;
        }
    }

    private NavTreeItemViewModel? FindConsoleNode(string consoleName) =>
        _navRoot?.Children.FirstOrDefault(c =>
            string.Equals(c.ConsoleName, consoleName, StringComparison.OrdinalIgnoreCase));

    private NavTreeItemViewModel? EnsureTreePathExists(string displayPath, string consoleName)
    {
        var consoleNode = FindConsoleNode(consoleName);
        if (consoleNode == null)
            return null;

        EnsureConsoleDrivesLoaded(consoleNode);

        var slash = displayPath.IndexOf('\\');
        if (slash < 0 || slash + 1 >= displayPath.Length)
            return consoleNode;

        var rest = displayPath[(slash + 1)..];
        var driveLetter = rest[0];
        var drivePath = WirePathService.BuildDriveDisplayPath(consoleName, driveLetter);
        var current = consoleNode.Children.FirstOrDefault(c =>
            string.Equals(c.DisplayPath, drivePath, StringComparison.OrdinalIgnoreCase));
        if (current == null)
            return consoleNode;

        var folderPart = rest.Length > 1 ? rest[1..].TrimStart('\\') : "";
        if (string.IsNullOrEmpty(folderPart))
            return current;

        foreach (var segment in folderPart.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var childPath = WirePathService.AppendDisplaySegment(current.DisplayPath, segment);
            var child = current.Children.FirstOrDefault(c =>
                string.Equals(c.DisplayPath, childPath, StringComparison.OrdinalIgnoreCase));
            if (child == null)
            {
                child = new NavTreeItemViewModel
                {
                    Title = segment,
                    Kind = NavigationNodeKind.Folder,
                    DisplayPath = childPath,
                    ConsoleName = consoleName,
                    Icon = ShellIconService.GetFolderIcon(),
                };
                current.Children.Add(child);
            }

            current = child;
        }

        return current;
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => LoadCurrentLocation();

    private async void OnAddConsoleClick(object? sender, RoutedEventArgs e)
    {
        var wizard = new AddConsoleWizardWindow(_consoleRegistry);
        await wizard.ShowDialog(this);
        if (wizard.Added)
            RefreshNavigationTree();
    }

    private void OnCutClick(object? sender, RoutedEventArgs e) => RunFileOp(() =>
    {
        var selection = BuildSelection();
        if (selection == null)
            return;
        _fileOps.Cut(selection);
        SetStatus($"Cut {selection.Items.Count} item(s).");
    });

    private void OnCopyClick(object? sender, RoutedEventArgs e) => RunFileOp(() =>
    {
        var selection = BuildSelection();
        if (selection == null)
            return;
        _fileOps.Copy(selection);
        SetStatus($"Copied {selection.Items.Count} item(s).");
    });

    private void OnPasteClick(object? sender, RoutedEventArgs e) => RunFileOp(() =>
    {
        if (string.IsNullOrWhiteSpace(_selectedConsole) || string.IsNullOrWhiteSpace(_currentDisplayPath))
            return;
        _fileOps.Paste(_selectedConsole, _currentDisplayPath, this);
        LoadCurrentLocation();
        SetStatus("Paste completed.");
    });

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var selection = BuildSelection();
        if (selection == null)
            return;

        await RunFileOpAsync(async () =>
        {
            await _fileOps.DeleteAsync(selection, this);
            LoadCurrentLocation();
            SetStatus("Delete completed.");
        });
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        var selection = BuildSelection();
        if (selection == null || selection.Items.Count != 1)
        {
            ShowInfo("Select exactly one item to rename.");
            return;
        }

        var currentName = selection.Items[0].Name;
        var newName = await PromptRenameAsync(currentName);
        if (string.IsNullOrWhiteSpace(newName) || newName == currentName)
            return;

        await RunFileOpAsync(() =>
        {
            _fileOps.Rename(selection, newName);
            LoadCurrentLocation();
            SetStatus("Rename completed.");
            return Task.CompletedTask;
        });
    }

    private void OnNewFolderClick(object? sender, RoutedEventArgs e) => RunFileOp(() =>
    {
        if (string.IsNullOrWhiteSpace(_selectedConsole) || string.IsNullOrWhiteSpace(_currentDisplayPath))
            return;
        _fileOps.CreateNewFolder(_selectedConsole, _currentDisplayPath);
        LoadCurrentLocation();
        SetStatus("Folder created.");
    });

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var selection = BuildSelection();
        if (selection == null)
            return;

        var folder = await PickLocalFolderAsync("Choose a folder on your PC to copy the selected items to:");
        if (folder == null)
            return;

        await RunFileOpAsync(() =>
        {
            _fileOps.ExportToPc(selection, folder);
            ShowInfo("Copy to PC completed.");
            SetStatus("Export completed.");
            return Task.CompletedTask;
        });
    }

    private void OnLaunchClick(object? sender, RoutedEventArgs e) => RunFileOp(() =>
    {
        var selection = BuildSelection();
        if (selection == null)
            return;
        _fileOps.LaunchXbe(selection);
        SetStatus("Launch command sent.");
    });

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            OnDeleteClick(sender, e);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            OnCopyClick(sender, e);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.X)
        {
            OnCutClick(sender, e);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
        {
            OnPasteClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Back && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            OnUpClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _ = ShowPropertiesAsync();
            e.Handled = true;
        }
    }

    private bool CanAcceptUpload() =>
        !string.IsNullOrWhiteSpace(_selectedConsole) &&
        !string.IsNullOrWhiteSpace(_currentDisplayPath) &&
        !WirePathService.IsDriveListing(_currentDisplayPath, _selectedConsole) &&
        !string.Equals(_currentDisplayPath, _selectedConsole, StringComparison.OrdinalIgnoreCase);

    private void OnFileGridDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = CanAcceptUpload() && e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnFileGridDrop(object? sender, DragEventArgs e)
    {
        if (!CanAcceptUpload())
        {
            ShowInfo("Open a drive or folder before dropping files.");
            return;
        }

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        var files = e.DataTransfer.TryGetFiles();
        if (files == null || files.Length == 0)
            return;

        var localPaths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();

        if (localPaths.Count == 0)
            return;

        await RunFileOpAsync(() =>
        {
            _fileOps.UploadFromPc(_selectedConsole!, _currentDisplayPath!, localPaths);
            LoadCurrentLocation();
            SetStatus($"Uploaded {localPaths.Count} item(s).");
            return Task.CompletedTask;
        });
    }

    private void OnFileGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(FileGrid).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(FileGrid);
            _dragPressArgs = e;
        }
    }

    private async void OnFileGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint == null)
            return;

        if (!e.GetCurrentPoint(FileGrid).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = null;
            _dragPressArgs = null;
            return;
        }

        var pos = e.GetPosition(FileGrid);
        var delta = pos - _dragStartPoint.Value;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
            return;

        var pressArgs = _dragPressArgs;
        _dragStartPoint = null;
        _dragPressArgs = null;
        if (pressArgs != null)
            await StartDragExportAsync(pressArgs);
    }

    private async Task StartDragExportAsync(PointerPressedEventArgs e)
    {
        var selection = BuildSelection();
        if (selection == null)
            return;

        if (WirePathService.IsDriveListing(_currentDisplayPath!, _selectedConsole!) &&
            selection.Items.All(i => i.Name.EndsWith(':')))
            return;

        try
        {
            SetStatus("Preparing items for drag...");
            var paths = _fileOps.PrepareDragExport(selection);
            if (paths.Count == 0)
                return;

            var storageItems = new List<IStorageItem>();
            foreach (var path in paths)
            {
                IStorageItem? item = Directory.Exists(path)
                    ? await StorageProvider.TryGetFolderFromPathAsync(path)
                    : await StorageProvider.TryGetFileFromPathAsync(path);
                if (item != null)
                    storageItems.Add(item);
            }

            if (storageItems.Count == 0)
                return;

            var transfer = new DataTransfer();
            foreach (var storageItem in storageItems)
                transfer.Add(DataTransferItem.CreateFile(storageItem));

            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy | DragDropEffects.Move);
            SetStatus("Drag completed.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private FileSelection? BuildSelection()
    {
        var selected = FileGrid.SelectedItems.Cast<FileRowViewModel>().ToList();
        if (selected.Count == 0 || string.IsNullOrWhiteSpace(_selectedConsole) || string.IsNullOrWhiteSpace(_currentDisplayPath))
            return null;

        var folderPath = WirePathService.IsDriveListing(_currentDisplayPath, _selectedConsole)
            ? _selectedConsole
            : _currentDisplayPath;

        var items = new List<FileSelectionItem>();
        foreach (var row in selected)
        {
            if (!TryGetWirePath(row, out var wirePath))
                continue;

            items.Add(new FileSelectionItem
            {
                Name = row.Name,
                WirePath = wirePath,
                IsDirectory = row.IsDirectory,
            });
        }

        if (items.Count == 0)
            return null;

        return new FileSelection
        {
            ConsoleName = _selectedConsole,
            FolderDisplayPath = folderPath,
            Items = items,
        };
    }

    private bool TryGetWirePath(FileRowViewModel row, out string wirePath)
    {
        wirePath = "";
        if (string.Equals(row.Type, "Drive", StringComparison.OrdinalIgnoreCase))
        {
            var display = WirePathService.BuildDriveDisplayPath(_selectedConsole!, row.Name[0]);
            return WirePathService.TryBuildWirePath(display, out wirePath);
        }

        return WirePathService.TryBuildWirePathInFolder(_currentDisplayPath!, row.Name, out wirePath);
    }

    private void RunFileOp(Action action)
    {
        try
        {
            action();
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task RunFileOpAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    public async Task<bool> ConfirmDeleteAsync(IReadOnlyList<Rxdk.Xbdm.KitServices.Models.FileSelectionItem> items)
    {
        var message = FormatDeleteMessage(items);
        var title = items.Count == 1 && items[0].IsDirectory
            ? "Confirm Folder Delete"
            : items.Count > 1
                ? "Confirm Multiple Delete"
                : "Confirm Delete";

        return await ShowYesNoDialogAsync(title, message);
    }

    private static string FormatDeleteMessage(IReadOnlyList<Rxdk.Xbdm.KitServices.Models.FileSelectionItem> items)
    {
        if (items.Count == 0)
            return "Are you sure you want to permanently delete this item?";

        if (items.Count == 1)
        {
            var name = items[0].Name;
            return items[0].IsDirectory
                ? $"Are you sure you want to remove the folder '{name}' and permanently delete its contents?"
                : $"Are you sure you want to permanently delete '{name}'?";
        }

        return $"Are you sure you want to permanently delete these '{items.Count}' items?";
    }

    public async Task<string?> PromptRenameAsync(string currentName)
    {
        var box = new TextBox { Text = currentName, Width = 280 };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "New name:" });
        panel.Children.Add(box);

        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Rename",
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        string? result = null;
        ok.Click += (_, _) =>
        {
            result = box.Text?.Trim();
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
        return result;
    }

    public async Task<string?> PickLocalFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public void ShowError(string message) => SetStatus(message);

    public void ShowInfo(string message) => SetStatus(message);

    private async Task<bool> ShowYesNoDialogAsync(string title, string message)
    {
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var yes = new Button { Content = "Yes", IsDefault = true };
        var no = new Button { Content = "No", IsCancel = true };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        var result = false;
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return result;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private async void OnPropertiesClick(object? sender, RoutedEventArgs e) => await ShowPropertiesAsync();

    private async void OnConsolePropertiesClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedConsole))
            return;

        var request = PropertyRequest.FromSelection(_selectedConsole, _selectedConsole, Array.Empty<FileSelectionItem>());
        if (request == null)
            return;

        await ShowPropertiesForRequestAsync(request);
    }

    private async Task ShowPropertiesAsync()
    {
        var selection = BuildSelection();
        var items = selection?.Items ?? Array.Empty<FileSelectionItem>();
        Rxdk.XbNeighborhood.Core.Services.PropertyRequest? request =
            Rxdk.XbNeighborhood.Core.Services.PropertyRequest.FromSelection(_selectedConsole, _currentDisplayPath, items);
        if (request == null)
        {
            request = Rxdk.XbNeighborhood.Core.Services.PropertyRequest.FromSelection(
                _selectedConsole,
                _currentDisplayPath,
                Array.Empty<FileSelectionItem>());
            if (request == null)
                return;
        }

        await ShowPropertiesForRequestAsync(request);
    }

    private async Task ShowPropertiesForRequestAsync(Rxdk.XbNeighborhood.Core.Services.PropertyRequest request, string? initialTabHeader = null)
    {
        try
        {
            var session = new PropertiesService().OpenProperties(request);
            var window = new PropertiesWindow(session, LoadCurrentLocation, initialTabHeader);
            await window.ShowDialog(this);
        }
        catch (XbdmException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private string? GetSelectedConsoleName()
    {
        if (NavTree.SelectedItem is NavTreeItemViewModel node)
        {
            var fromTree = GetConsoleNameFromTreeItem(node);
            if (!string.IsNullOrWhiteSpace(fromTree))
                return fromTree;
        }

        return _selectedConsole;
    }

    private void OnRemoveConsoleClick(object? sender, RoutedEventArgs e)
    {
        if (NavTree.SelectedItem is not NavTreeItemViewModel node || node.Kind != NavigationNodeKind.Console)
        {
            ShowInfo("Select a console in the tree to remove.");
            return;
        }

        var name = node.ConsoleName;
        if (string.IsNullOrWhiteSpace(name))
            return;

        var defaultName = _consoleRegistry.GetDefaultConsoleName();
        if (string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Cannot remove the default console. Set another default first.");
            return;
        }

        _consoleRegistry.RemoveConsole(name);
        _selectedConsole = null;
        _currentDisplayPath = null;
        RefreshNavigationTree();
        _rows.Clear();
        PathText.Text = "";
        SetStatus($"Removed console '{name}'.");
    }

    private void OnSetDefaultConsoleClick(object? sender, RoutedEventArgs e)
    {
        var name = GetSelectedConsoleName();
        if (string.IsNullOrWhiteSpace(name))
            return;

        RunFileOp(() =>
        {
            _consoleRegistry.SetDefaultConsole(name);
            RefreshNavigationTree();
            SetStatus($"'{name}' is now the default console.");
        });
    }

    private void OnConsoleRebootWarmClick(object? sender, RoutedEventArgs e) =>
        RebootSelectedConsole(cold: false, sameTitle: false);

    private void OnConsoleRebootColdClick(object? sender, RoutedEventArgs e) =>
        RebootSelectedConsole(cold: true, sameTitle: false);

    private void OnConsoleRebootSameClick(object? sender, RoutedEventArgs e) =>
        RebootSelectedConsole(cold: false, sameTitle: true);

    private void RebootSelectedConsole(bool cold, bool sameTitle)
    {
        var name = GetSelectedConsoleName();
        if (string.IsNullOrWhiteSpace(name))
            return;

        RunFileOp(() =>
        {
            string? launch = null;
            if (sameTitle)
            {
                using var conn = XbdmSession.Connect(name);
                launch = conn.GetXbeLaunchPath();
            }

            _fileOps.Reboot(name, cold, launch);
            SetStatus("Reboot command sent.");
        });
    }

    private async void OnConsoleScreenshotClick(object? sender, RoutedEventArgs e)
    {
        var name = GetSelectedConsoleName();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save screenshot",
            DefaultExtension = "bmp",
            SuggestedFileName = $"{name}-screenshot.bmp",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Bitmap") { Patterns = new[] { "*.bmp" } },
            },
        });

        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        RunFileOp(() =>
        {
            using var conn = XbdmSession.Connect(name);
            conn.CaptureScreenshot(path);
            SetStatus("Screenshot saved.");
        });
    }

    private async void OnConsoleSecurityClick(object? sender, RoutedEventArgs e)
    {
        var name = GetSelectedConsoleName();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var request = PropertyRequest.FromSelection(name, name, Array.Empty<FileSelectionItem>());
        if (request == null)
            return;

        await ShowPropertiesForRequestAsync(request, "Security");
    }
}
