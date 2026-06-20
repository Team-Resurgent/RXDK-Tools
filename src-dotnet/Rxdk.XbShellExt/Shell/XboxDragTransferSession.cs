using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Ui.Forms;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbShellExt.Shell;

internal sealed class XboxDragTransferSession : IDisposable
{
    private readonly XbdmConnection _connection;
    private readonly int _fileCount;
    private readonly ulong _totalBytes;
    private ulong _completedBytes;
    private int _activeStreams;
    private int _filesCompleted;
    private bool _disposed;
    private TransferProgressForm? _progress;
    private Thread? _progressThread;
    private readonly ManualResetEventSlim _progressReady = new(false);

    public XboxDragTransferSession(IReadOnlyList<XboxDragEntry> catalog, XbdmConnection connection)
    {
        _connection = connection;
        _fileCount = catalog.Count(entry => !entry.IsDirectory);
        _totalBytes = catalog.Where(entry => !entry.IsDirectory).Aggregate(0UL, (sum, entry) => sum + entry.Size);
    }

    public INativeComStream? OpenStream(XboxDragEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (entry.IsDirectory)
            return null;

        EnsureProgressShown();
        var lease = BeginFile(entry.RelativePath);
        return new XbdmReceiveComStream(_connection, entry, lease);
    }

    public StreamLease BeginFile(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _activeStreams);
        return new StreamLease(this, relativePath);
    }

    internal void SetCurrentFile(string relativePath)
    {
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

    internal void ReportBytes(int count)
    {
        if (count <= 0)
            return;

        _completedBytes += (ulong)count;
        var progress = _progress;
        if (progress == null || progress.IsDisposed)
            return;

        try
        {
            progress.BeginInvoke(() => progress.ReportBytes(_completedBytes, _totalBytes));
        }
        catch
        {
        }
    }

    internal void ThrowIfCancelled()
    {
        if (_progress?.IsCancelRequested == true)
            throw new OperationCanceledException();
    }

    internal void EndFile(StreamLease lease, bool completedSuccessfully)
    {
        if (completedSuccessfully)
            Interlocked.Increment(ref _filesCompleted);

        if (Interlocked.Decrement(ref _activeStreams) == 0 &&
            _filesCompleted >= _fileCount)
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopProgressUi();
        _connection.Dispose();
        _progressReady.Dispose();
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
            form.Configure(_totalBytes, _fileCount);
            form.FormClosed += (_, _) => Application.ExitThread();
            _progress = form;
            _progressReady.Set();
            form.Show();
            Application.Run(form);
        });
        _progressThread.SetApartmentState(ApartmentState.STA);
        _progressThread.IsBackground = true;
        _progressThread.Start();
        _progressReady.Wait();
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

    internal sealed class StreamLease : IDisposable
    {
        private XboxDragTransferSession? _session;
        private bool _completedSuccessfully;

        internal StreamLease(XboxDragTransferSession session, string relativePath)
        {
            _session = session;
            RelativePath = relativePath;
            session.SetCurrentFile(relativePath);
        }

        public string RelativePath { get; }

        public void ReportBytes(int count) => _session?.ReportBytes(count);

        public void ThrowIfCancelled() => _session?.ThrowIfCancelled();

        public void MarkCompleted() => _completedSuccessfully = true;

        public void Dispose()
        {
            var session = Interlocked.Exchange(ref _session, null);
            session?.EndFile(this, _completedSuccessfully);
        }
    }
}
