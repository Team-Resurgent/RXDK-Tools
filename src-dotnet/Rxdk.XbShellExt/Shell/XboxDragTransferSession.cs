using Rxdk.XbShellExt.Diagnostics;
using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Ui;
using Rxdk.XbShellExt.Ui.Forms;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.Managed;
using System.Globalization;
namespace Rxdk.XbShellExt.Shell;

internal sealed class XboxDragTransferSession : IDisposable
{
    private readonly XbdmConnection _connection;
    private readonly SemaphoreSlim _getFileGate = new(1, 1);
    private readonly int _fileCount;
    private readonly ulong _totalBytes;
    private ulong _completedBytes;
    private ulong _currentFileBytes;
    private ulong _currentFileSize;
    private int _activeStreams;
    private int _filesCompleted;
    private bool _disposed;
    private int _failureReported;
    private bool _transferFailed;
    private int _cancelRequested;
    private int _comStreamHolds;
    private int _activityEnded;
    private int _ownerReleased;
    private int _disposeRequested;
    private TransferProgressForm? _progress;
    private Thread? _progressThread;
    private readonly ManualResetEventSlim _progressReady = new(false);
    private string? _currentRelativePath;
    private readonly object _streamRegistryLock = new();
    private readonly List<WeakReference<XbdmReceiveComStream>> _liveStreams = new();
    private readonly HashSet<string> _completedWirePaths = new(StringComparer.OrdinalIgnoreCase);

    public static (XboxDragTransferSession Session, IReadOnlyList<XboxDragEntry> Catalog) Start(FileSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        IReadOnlyList<XboxDragEntry> catalog;
        using (var catalogConnection = XbdmSession.Connect(selection.ConsoleName))
        {
            catalog = XboxDragCatalog.Build(catalogConnection, selection);
        }

        // Catalog enumeration hammers the connection with DIRLIST/GETFILEATTRIBUTES.
        // Use a fresh connection for GETFILE transfers so the session starts clean.
        var transferConnection = XbdmSession.Connect(selection.ConsoleName);
        try
        {
            var session = new XboxDragTransferSession(catalog, transferConnection);
            ShellTransferActivity.Begin();
            return (session, catalog);
        }
        catch
        {
            transferConnection.Dispose();
            throw;
        }
    }

    private XboxDragTransferSession(IReadOnlyList<XboxDragEntry> catalog, XbdmConnection connection)
    {
        var files = catalog.Where(entry => !entry.IsDirectory).ToList();
        _fileCount = files.Count;
        _totalBytes = files.Aggregate(0UL, (sum, entry) => sum + entry.Size);
        _connection = connection;
    }

    public INativeComStream? OpenStream(XboxDragEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (entry.IsDirectory)
            return null;

        if (_completedWirePaths.Contains(entry.WirePath))
            return new CompletedDragComStream(entry, this);

        AbortStreamsForPath(entry.WirePath);

        // Defer until Explorer opens the first content stream (after replace prompts).
        EnsureProgressShown();
        var lease = BeginFile(entry.RelativePath, entry.WirePath, entry.Size);
        var stream = new XbdmReceiveComStream(_connection, entry, lease, _getFileGate, this);
        NotifyComStreamHeld();
        return stream;
    }

    internal void NotifyComStreamHeld() => Interlocked.Increment(ref _comStreamHolds);

    internal void NotifyComStreamReleased()
    {
        if (Interlocked.Decrement(ref _comStreamHolds) == 0)
            TryCompleteSession();
    }

    internal void NotifyOwnerReleased()
    {
        Volatile.Write(ref _ownerReleased, 1);
        TryCompleteSession();
    }

    internal void PrepareForReplacement()
    {
        Volatile.Write(ref _disposeRequested, 1);
        AbortAllStreams();
        TryCompleteSession();
    }

    internal bool IsWirePathCompleted(string wirePath)
    {
        lock (_completedWirePaths)
            return _completedWirePaths.Contains(wirePath);
    }

    internal void RecordCompletedWirePath(string wirePath, XbdmReceiveComStream? except = null)
    {
        lock (_completedWirePaths)
            _completedWirePaths.Add(wirePath);

        List<XbdmReceiveComStream>? targets = null;
        lock (_streamRegistryLock)
        {
            PruneDeadStreams();
            foreach (var weak in _liveStreams)
            {
                if (!weak.TryGetTarget(out var stream))
                    continue;

                if (except != null && ReferenceEquals(stream, except))
                    continue;

                if (!string.Equals(stream.WirePath, wirePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                targets ??= new List<XbdmReceiveComStream>();
                targets.Add(stream);
            }
        }

        if (targets == null)
            return;

        foreach (var stream in targets)
        {
            ManagedTrace.Line($"TransferAbort releasing duplicate stream path='{wirePath}'");
            stream.Abort();
        }
    }

    internal void RegisterStream(XbdmReceiveComStream stream)
    {
        lock (_streamRegistryLock)
        {
            PruneDeadStreams();
            _liveStreams.Add(new WeakReference<XbdmReceiveComStream>(stream));
        }
    }

    private void AbortStreamsForPath(string wirePath)
    {
        List<XbdmReceiveComStream>? targets = null;
        lock (_streamRegistryLock)
        {
            PruneDeadStreams();
            foreach (var weak in _liveStreams)
            {
                if (!weak.TryGetTarget(out var stream))
                    continue;

                if (!string.Equals(stream.WirePath, wirePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                targets ??= new List<XbdmReceiveComStream>();
                targets.Add(stream);
            }
        }

        if (targets == null)
            return;

        foreach (var stream in targets)
        {
            ManagedTrace.Line($"TransferAbort superseding prior stream path='{wirePath}'");
            stream.Abort();
        }
    }

    private void PruneDeadStreams()
    {
        for (var i = _liveStreams.Count - 1; i >= 0; i--)
        {
            if (!_liveStreams[i].TryGetTarget(out _))
                _liveStreams.RemoveAt(i);
        }
    }

    public StreamLease BeginFile(string relativePath, string wirePath, ulong catalogSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _activeStreams);
        BeginCurrentFile(relativePath, catalogSize);
        return new StreamLease(this, relativePath, wirePath, catalogSize);
    }

    private void BeginCurrentFile(string relativePath, ulong catalogSize)
    {
        _currentRelativePath = relativePath;
        _currentFileBytes = 0;
        _currentFileSize = catalogSize;
        SetCurrentFile(relativePath);
        RefreshProgress(async: true);
    }

    internal void ReportCurrentFileProgress(StreamLease lease, ulong bytesRead, ulong streamTotal = 0)
    {
        if (lease.IsEnded)
            return;

        lease.BytesRead = bytesRead;
        _currentRelativePath = lease.RelativePath;
        _currentFileBytes = bytesRead;
        _currentFileSize = ProgressFileSize(lease.CatalogSize, streamTotal);
        RefreshProgress(async: true);
    }

    private static ulong ProgressFileSize(ulong catalogSize, ulong streamTotal)
    {
        if (catalogSize == 0)
            return streamTotal;

        if (streamTotal == 0)
            return catalogSize;

        return Math.Min(catalogSize, streamTotal);
    }

    private int FilesPercent()
    {
        if (_filesCompleted >= _fileCount)
            return 100;

        if (_fileCount <= 1)
            return CurrentFilePercent();

        if (_totalBytes > 0)
            return (int)Math.Min(100UL, ((_completedBytes + _currentFileBytes) * 100UL + _totalBytes - 1) / _totalBytes);

        return (int)Math.Min(100, _filesCompleted * 100 / Math.Max(1, _fileCount));
    }

    private int CurrentFilePercent() =>
        _currentFileSize == 0
            ? (_currentFileBytes > 0 ? 100 : 0)
            : (int)Math.Min(100UL, _currentFileBytes * 100UL / _currentFileSize);

    private void RefreshProgress(bool async, int? filePercentOverride = null, int? filesPercentOverride = null)
    {
        var progress = _progress;
        if (progress == null || progress.IsDisposed)
            return;

        var filesPct = filesPercentOverride ?? FilesPercent();
        var filePct = filePercentOverride ?? CurrentFilePercent();
        try
        {
            void Update() => progress.ReportProgress(filesPct, filePct, _filesCompleted, _fileCount);
            if (async && progress.InvokeRequired)
                progress.BeginInvoke(Update);
            else if (progress.InvokeRequired)
                progress.Invoke(Update);
            else
                Update();
        }
        catch
        {
        }
    }
    internal void SetCurrentFile(string relativePath)
    {
        _currentRelativePath = relativePath;
        var progress = _progress;
        if (progress == null || progress.IsDisposed)
            return;

        try
        {
            progress.BeginInvoke(() => progress.SetCurrentFile(relativePath));
        }
        catch
        {
        }
    }

    internal void ThrowIfCancelled()
    {
        if (Volatile.Read(ref _cancelRequested) != 0)
            throw new OperationCanceledException();
    }

    internal bool IsCancelRequested => Volatile.Read(ref _cancelRequested) != 0;

    internal void RequestCancel()
    {
        var message = BuildCancelledMessage();
        var first = Interlocked.CompareExchange(ref _cancelRequested, 1, 0) == 0;
        if (first)
        {
            ManagedTrace.Line("TransferCancel requested");
            AbortAllStreams();
            _transferFailed = true;
            ComErrorInfo.SetMessage(message);
            Interlocked.CompareExchange(ref _failureReported, 1, 0);
        }

        CloseProgressUi();
    }

    private void AbortAllStreams()
    {
        List<XbdmReceiveComStream>? targets = null;
        lock (_streamRegistryLock)
        {
            PruneDeadStreams();
            foreach (var weak in _liveStreams)
            {
                if (!weak.TryGetTarget(out var stream))
                    continue;

                targets ??= new List<XbdmReceiveComStream>();
                targets.Add(stream);
            }
        }

        if (targets == null)
            return;

        foreach (var stream in targets)
            stream.FailCancelled();
    }

    internal void ReportFailure(string? detail, bool cancelled = false)
    {
        if (Interlocked.CompareExchange(ref _failureReported, 1, 0) != 0)
            return;

        _transferFailed = true;
        var message = cancelled
            ? BuildCancelledMessage()
            : BuildFailureMessage(detail);
        ManagedTrace.Line($"TransferFailed: {message}");
        ComErrorInfo.SetMessage(message);

        if (cancelled)
            CloseProgressUi();
        else if (_progress is { IsDisposed: false } progress)
        {
            try
            {
                RunOnProgressThread(progress, form => form.Fail(message));
            }
            catch
            {
            }
        }
        else
        {
            ShellUiHost.ShowError(IntPtr.Zero, message);
        }

        TryFinalizeBackendWhenIdle();
    }

    private void CloseProgressUi()
    {
        try
        {
            StopProgressUi();
        }
        catch
        {
        }
    }

    private string BuildCancelledMessage()
    {
        if (_filesCompleted > 0 && _fileCount > 1)
            return $"The transfer was cancelled after {_filesCompleted} of {_fileCount} files.";

        return "The transfer was cancelled.";
    }

    private string BuildFailureMessage(string? detail)
    {
        var reason = string.IsNullOrWhiteSpace(detail)
            ? "The transfer could not be completed."
            : detail.Trim();

        if (_filesCompleted > 0 && _fileCount > 1)
            return $"The transfer stopped after {_filesCompleted} of {_fileCount} files.\r\n\r\n{reason}";

        return reason;
    }

    internal void EndFile(StreamLease lease, bool completedSuccessfully)
    {
        if (completedSuccessfully)
        {
            var fileBytes = lease.CatalogSize > 0 ? lease.CatalogSize : lease.BytesRead;
            if (fileBytes > 0)
                _completedBytes += fileBytes;

            _currentFileBytes = 0;
            _currentFileSize = 0;

            Interlocked.Increment(ref _filesCompleted);
            var filesPct = _filesCompleted >= _fileCount
                ? 100
                : (_totalBytes > 0
                    ? (int)Math.Min(100UL, (_completedBytes * 100UL + _totalBytes - 1) / _totalBytes)
                    : FilesPercent());
            ManagedTrace.Line(
                $"TransferProgress file='{lease.RelativePath}' bytes={fileBytes} " +
                $"filePct=100 overallPct={filesPct} files={_filesCompleted}/{_fileCount}");
            RefreshProgress(async: false, filePercentOverride: 100, filesPercentOverride: filesPct);
            MaybeDebugDelayAfterFile();
        }

        if (Interlocked.Decrement(ref _activeStreams) == 0)
            TryCompleteSession();
    }

    private void TryCompleteSession()
    {
        if (_activeStreams > 0 || Volatile.Read(ref _comStreamHolds) > 0)
            return;

        if (_transferFailed || IsCancelRequested || Volatile.Read(ref _ownerReleased) != 0 ||
            Volatile.Read(ref _disposeRequested) != 0)
        {
            if (_backendDisposed == 0)
                TryFinalizeBackendWhenIdle();
            return;
        }

        if (_filesCompleted >= _fileCount)
        {
            CompleteProgress();
            FinishSession();
        }
    }

    private void FinishSession()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopProgressUi();
        FinalizeBackend();
        EndTransferActivity();
        try { _progressReady.Dispose(); } catch { }
    }

    private void EndTransferActivity()
    {
        if (Interlocked.CompareExchange(ref _activityEnded, 1, 0) != 0)
            return;

        ShellTransferActivity.End();
    }
    private void MaybeDebugDelayAfterFile()
    {
        var delayText = Environment.GetEnvironmentVariable("XB_TRANSFER_DEBUG_DELAY");
        if (!int.TryParse(delayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0)
        {
            return;
        }

        var progress = _progress;
        if (progress == null || progress.IsDisposed)
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            return;
        }

        try
        {
            RunOnProgressThread(progress, form =>
            {
                var until = Environment.TickCount64 + (seconds * 1000L);
                while (Environment.TickCount64 < until && !form.IsDisposed)
                {
                    Application.DoEvents();
                    Thread.Sleep(50);
                }
            });
        }
        catch
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }
    }

    private void CompleteProgress()
    {
        var progress = _progress;
        if (progress == null || progress.IsDisposed)
            return;

        try
        {
            RunOnProgressThread(progress, static form => form.Complete());
        }
        catch
        {
        }
    }

    private int _backendDisposed;

    public void Dispose()
    {
        Volatile.Write(ref _disposeRequested, 1);
        AbortAllStreams();
        TryCompleteSession();
    }

    private void TryFinalizeBackendWhenIdle()
    {
        if (_activeStreams > 0 || Volatile.Read(ref _comStreamHolds) > 0 || _backendDisposed != 0)
            return;

        FinalizeBackend();
        if (!_disposed)
        {
            _disposed = true;
            StopProgressUi();
            try { _progressReady.Dispose(); } catch { }
        }

        EndTransferActivity();
    }

    private void FinalizeBackend()
    {
        if (Interlocked.CompareExchange(ref _backendDisposed, 1, 0) != 0)
            return;

        _connection.Dispose();
        _getFileGate.Dispose();
    }

    private void OnProgressFormClosed()
    {
        TryFinalizeBackendWhenIdle();
    }

    private void EnsureProgressShown()
    {
        if (_progress != null || _fileCount == 0)
            return;

        _progressThread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new TransferProgressForm("Copying from Xbox");
            form.Configure(_fileCount);
            form.CancelRequested += RequestCancel;
            form.CloseRequested += RequestCancel;
            form.FormClosed += (_, _) =>
            {
                OnProgressFormClosed();
                Application.ExitThread();
            };
            _progress = form;
            _progressReady.Set();
            form.Show();
            form.BringToFront();
            form.Activate();

            if (!string.IsNullOrEmpty(_currentRelativePath))
                form.SetCurrentFile(_currentRelativePath);
            form.ReportProgress(FilesPercent(), CurrentFilePercent(), _filesCompleted, _fileCount);

            Application.Run(form);
        });
        _progressThread.SetApartmentState(ApartmentState.STA);
        _progressThread.IsBackground = true;
        _progressThread.Start();
        _progressReady.Wait(TimeSpan.FromSeconds(3));
    }

    private void StopProgressUi()
    {
        var progress = _progress;
        if (progress == null || progress.IsDisposed)
            return;

        try
        {
            if (progress.InvokeRequired)
                progress.BeginInvoke(progress.Close);
            else
                progress.Close();
        }
        catch
        {
        }

        _progressThread?.Join(TimeSpan.FromSeconds(2));
        _progress = null;
        _progressThread = null;
    }

    private static void RunOnProgressThread(TransferProgressForm progress, Action<TransferProgressForm> action)
    {
        if (progress.InvokeRequired)
            progress.BeginInvoke(action, progress);
        else
            action(progress);
    }

    internal sealed class StreamLease : IDisposable
    {
        private XboxDragTransferSession? _session;
        private int _ended;
        private bool _completedSuccessfully;

        internal StreamLease(XboxDragTransferSession session, string relativePath, string wirePath, ulong catalogSize)
        {
            _session = session;
            RelativePath = relativePath;
            WirePath = wirePath;
            CatalogSize = catalogSize;
        }

        public string RelativePath { get; }

        public string WirePath { get; }

        public ulong CatalogSize { get; }

        public ulong BytesRead { get; set; }

        internal bool IsEnded => Volatile.Read(ref _ended) != 0;

        public void ReportCurrentFileProgress(ulong bytesRead, ulong streamTotal = 0) =>
            _session?.ReportCurrentFileProgress(this, bytesRead, streamTotal);

        public void ThrowIfCancelled() => _session?.ThrowIfCancelled();

        public void ReportTransferFailure(string? detail, bool cancelled = false) =>
            _session?.ReportFailure(detail, cancelled);

        public void MarkCompleted()
        {
            _completedSuccessfully = true;
            TryEndFile();
        }

        public void Dispose() => TryEndFile();

        private void TryEndFile()
        {
            if (Interlocked.CompareExchange(ref _ended, 1, 0) != 0)
                return;

            var session = Interlocked.Exchange(ref _session, null);
            session?.EndFile(this, _completedSuccessfully);
        }
    }
}