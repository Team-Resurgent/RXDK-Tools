using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Rxdk.XbWatson.Core;
using Rxdk.XbWatson.Core.BreakInfo;
using Rxdk.XbWatson.Dialogs;

namespace Rxdk.XbWatson;

public partial class MainWindow : Window, IWatsonEventSink
{
    private readonly WatsonLogBuffer _logBuffer = new();
    private readonly WatsonSession _session;
    private readonly string _consoleName;
    private readonly object _logGate = new();
    private readonly DispatcherTimer _logFlushTimer;
    private bool _logDirty;

    public MainWindow(string consoleName)
    {
        _consoleName = consoleName;
        _session = new WatsonSession(this);
        InitializeComponent();
        Title = $"xbWatson - Log Window [{_consoleName}]";

        _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _logFlushTimer.Tick += (_, _) => FlushLogIfDirty();

        if (IsTimestampsEnabledByEnvironment())
        {
            _session.TimestampsEnabled = true;
            TimestampsCheckMark.IsVisible = true;
        }

        Opened += OnOpened;
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _logFlushTimer.Stop();
        _session.Dispose();
    }

    private static bool IsTimestampsEnabledByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(WatsonKernelDebug.TimestampsEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(value)
            && int.TryParse(value, out var parsed)
            && parsed != 0;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (!_session.TryStart(_consoleName))
        {
            var message = string.IsNullOrWhiteSpace(_session.StartError)
                ? "Failed to open notification session with the Xbox."
                : $"Failed to open notification session with the Xbox.\n\n{_session.StartError}";
            var dialog = new Window
            {
                Title = "xbWatson",
                Width = 420,
                Height = 200,
                Content = new TextBlock
                {
                    Text = message,
                    Margin = new Avalonia.Thickness(16),
                },
            };
            dialog.Show();
            Close();
            return;
        }

        _logFlushTimer.Start();
    }

    public void OnXboxConnected()
    {
        Dispatcher.UIThread.Post(UpdateKernelDebugMenu);
    }

    private void UpdateKernelDebugMenu()
    {
        KernelDebugMenuItem.IsEnabled = _session.KernelDebugAvailable;
        KernelDebugCheckMark.IsVisible = _session.KernelDebugOutputEnabled;
    }

    public void OnLog(string text)
    {
        lock (_logGate)
        {
            _logBuffer.Append(text);
            _logDirty = true;
        }
    }

    private void FlushLogIfDirty()
    {
        if (!_logDirty)
            return;

        string text;
        lock (_logGate)
        {
            if (!_logDirty)
                return;
            text = _logBuffer.Text;
            _logDirty = false;
        }

        LogTextBox.Text = text;
        LogTextBox.CaretIndex = text.Length;
    }

    public void OnWarning(string text) => OnLog(text);

    public async Task<WatsonAssertChoice> OnAssertAsync(WatsonAssertEvent evt)
    {
        var tcs = new TaskCompletionSource<WatsonAssertChoice>();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            WatsonBeep.Exclamation();
            var dialog = new AssertDialog(evt);
            var result = await dialog.ShowDialog<WatsonAssertChoice>(this);
            tcs.TrySetResult(result);
        });
        return await tcs.Task;
    }

    public async Task<WatsonRipChoice> OnRipAsync(WatsonRipEvent evt)
    {
        var tcs = new TaskCompletionSource<WatsonRipChoice>();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            WatsonBeep.Exclamation();
            var dialog = new RipDialog(evt, TrySaveCrashDumpAsync);
            var result = await dialog.ShowDialog<WatsonRipChoice>(this);
            tcs.TrySetResult(result);
        });
        return await tcs.Task;
    }

    public async Task<WatsonExceptionChoice> OnExceptionAsync(WatsonExceptionEvent evt)
    {
        var tcs = new TaskCompletionSource<WatsonExceptionChoice>();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            WatsonBeep.Exclamation();
            var dialog = new ExceptionDialog(evt, TrySaveCrashDumpAsync);
            var result = await dialog.ShowDialog<WatsonExceptionChoice>(this);
            tcs.TrySetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<bool> TrySaveCrashDumpAsync(uint threadId, uint eventType, uint eventCode, bool writeViolation, uint avAddress, string? ripText)
    {
        var path = await PickSavePath("Log Files (*.LOG)", new[] { "log" }, "log");
        if (path == null)
            return false;

        try
        {
            if (!_session.TrySaveCrashDump(threadId, eventType, eventCode, writeViolation, avAddress, ripText, path))
            {
                await ShowMessage("Unable to get crash information", "Failed to obtain crash information from application.  Log will not be saved");
                return false;
            }
            return true;
        }
        catch (IOException ex) when (ex.Message.Contains("disk", StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessage("Insufficient disk space", "The specified drive is full.  Please free some space on it or select another drive, and try again.");
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            await ShowMessage("Cannot overwrite file", "The specified file cannot be overwritten.  Please ensure the file isn't in use and try again.");
            return false;
        }
        catch
        {
            await ShowMessage("Cannot save file", "xbWatson cannot save the log to the specified location.  Please check the device and try again.");
            return false;
        }
    }

    private async Task<string?> PickSavePath(string title, string[] extensions, string defaultExt)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExt,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(title) { Patterns = extensions.Select(e => $"*.{e}").ToList() },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } },
            },
        });
        return file?.TryGetLocalPath();
    }

    private async Task ShowMessage(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 160,
            Content = new TextBlock { Text = message, Margin = new Avalonia.Thickness(16) },
        };
        await dialog.ShowDialog(this);
    }

    private void OnLogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl && e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
        {
            WatsonBeep.Beep();
            e.Handled = true;
        }
    }

    private async void OnSaveLog(object? sender, RoutedEventArgs e)
    {
        var path = await PickSavePath("Text Files (*.TXT)", new[] { "txt" }, "txt");
        if (path == null)
            return;

        try
        {
            string text;
            lock (_logGate)
            {
                text = _logBuffer.Text;
            }

            if (text.Length > 65535)
                text = text[..65535];
            text = text.Replace("\n", Environment.NewLine);
            await File.WriteAllTextAsync(path, text);
        }
        catch
        {
            await ShowMessage("Cannot save file", "xbWatson cannot save the log to the specified location.  Please check the device and try again.");
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;
        var selection = LogTextBox.SelectedText;
        if (!string.IsNullOrEmpty(selection))
            await clipboard.SetTextAsync(selection);
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        lock (_logGate)
        {
            _logBuffer.Clear();
            _logDirty = false;
        }
        LogTextBox.Text = string.Empty;
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        LogTextBox.SelectAll();
        LogTextBox.Focus();
    }

    private void OnToggleTimestamps(object? sender, RoutedEventArgs e)
    {
        var enabled = !TimestampsCheckMark.IsVisible;
        TimestampsCheckMark.IsVisible = enabled;
        _session.TimestampsEnabled = enabled;
    }

    private void OnToggleKernelDebug(object? sender, RoutedEventArgs e)
    {
        var desired = !KernelDebugCheckMark.IsVisible;
        KernelDebugCheckMark.IsVisible = desired;
        if (!_session.TrySetKernelDebugOutput(desired))
            KernelDebugCheckMark.IsVisible = !desired;
    }

    private void OnToggleLimitBuffer(object? sender, RoutedEventArgs e)
    {
        var enabled = !LimitBufferCheckMark.IsVisible;
        LimitBufferCheckMark.IsVisible = enabled;
        _logBuffer.LimitBufferLength = enabled;
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var about = new AboutDialog();
        await about.ShowDialog(this);
    }
}
