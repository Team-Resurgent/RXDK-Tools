using Rxdk.XbShellExt.Diagnostics;
using Rxdk.XbShellExt.Ui;
using Rxdk.XbShellExt.Ui.Forms;
using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbShellExt.Shell;

internal static class XboxUploadTransferSession
{
    public static void Run(string consoleName, string targetFolderDisplayPath, IReadOnlyList<string> localPaths)
    {
        if (localPaths.Count == 0)
            return;

        if (WirePathService.IsDriveListing(targetFolderDisplayPath, consoleName) ||
            string.Equals(targetFolderDisplayPath, consoleName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Open a drive or folder before dropping files.");

        if (!WirePathService.TryBuildWirePath(targetFolderDisplayPath, out var wireFolder))
            throw new InvalidOperationException("Invalid target folder.");

        using var session = new Session(consoleName, wireFolder, localPaths);
        session.Execute();
    }

    private sealed class Session : IDisposable
    {
        private readonly string _consoleName;
        private readonly string _wireFolder;
        private readonly IReadOnlyList<string> _localPaths;
        private IReadOnlyList<LocalUploadEntry> _catalog = Array.Empty<LocalUploadEntry>();
        private long _completedBytes;
        private long _currentFileBytes;
        private long _currentFileSize;
        private int _filesCompleted;
        private int _cancelRequested;
        private int _activityEnded;
        private int _failureUiShown;
        private Exception? _workerError;
        private TransferProgressForm? _progress;
        private Thread? _progressThread;
        private readonly ManualResetEventSlim _progressReady = new(false);

        internal Session(string consoleName, string wireFolder, IReadOnlyList<string> localPaths)
        {
            _consoleName = consoleName;
            _wireFolder = wireFolder;
            _localPaths = localPaths;
        }

        public void Execute()
        {
            ShellTransferActivity.Begin();
            try
            {
                EnsureProgressShown("Preparing upload…");
                var worker = new Thread(UploadWorker)
                {
                    IsBackground = true,
                    Name = "XboxUploadTransfer",
                };
                worker.Start();
                worker.Join();

                if (_workerError is OperationCanceledException)
                    return;

                if (_workerError != null)
                    throw new InvalidOperationException(_workerError.Message, _workerError);

                CompleteProgress();
            }
            finally
            {
                if (Volatile.Read(ref _failureUiShown) == 0)
                    StopProgressUi();
                EndTransferActivity();
            }
        }

        private void UploadWorker()
        {
            try
            {
                _catalog = LocalUploadCatalog.Build(_localPaths, _wireFolder);
                if (_catalog.Count == 0)
                {
                    SetCurrentFile("Nothing to upload.");
                    return;
                }

                ConfigureProgress(_catalog.Count);
                using var conn = XbdmSession.Connect(_consoleName);
                foreach (var entry in _catalog)
                {
                    ThrowIfCancelled();
                    BeginCurrentFile(entry);
                    if (entry.IsDirectory)
                    {
                        CreateWireDirectory(conn, entry.WirePath);
                        CompleteCurrentFile(entry);
                        continue;
                    }

                    EnsureWireDirectories(conn, entry.WirePath);
                    conn.SendFile(
                        entry.LocalPath,
                        entry.WirePath,
                        OnFileProgress,
                        () => Volatile.Read(ref _cancelRequested) != 0);
                    CompleteCurrentFile(entry);
                }
            }
            catch (Exception ex)
            {
                _workerError = ex;
                if (ex is not OperationCanceledException)
                {
                    ManagedTrace.Line($"UploadFailed: {ex.Message}");
                    ShowFailure(ex.Message);
                }
                else
                {
                    ManagedTrace.Line("UploadCancel requested");
                }
            }
        }

        private void BeginCurrentFile(LocalUploadEntry entry)
        {
            _currentFileBytes = 0;
            _currentFileSize = entry.Size;
            SetCurrentFile(entry.RelativePath);
            RefreshProgress();
        }

        private void OnFileProgress(long sent, long total)
        {
            _currentFileBytes = sent;
            _currentFileSize = total;
            RefreshProgress(async: true);
        }

        private void CompleteCurrentFile(LocalUploadEntry entry)
        {
            var fileBytes = entry.Size > 0 ? entry.Size : _currentFileBytes;
            if (fileBytes > 0)
                _completedBytes += fileBytes;

            _currentFileBytes = 0;
            _currentFileSize = 0;
            Interlocked.Increment(ref _filesCompleted);
            RefreshProgress(filesPercentOverride: FilesPercent(), filePercentOverride: 100);
            ManagedTrace.Line(
                $"UploadProgress file='{entry.RelativePath}' bytes={fileBytes} " +
                $"files={_filesCompleted}/{_catalog.Count}");
        }

        private int FilesPercent()
        {
            if (_filesCompleted >= _catalog.Count)
                return 100;

            if (_catalog.Count <= 1)
                return CurrentFilePercent();

            var totalBytes = _catalog.Aggregate(0L, (sum, entry) => sum + entry.Size);
            if (totalBytes > 0)
                return (int)Math.Min(100L, ((_completedBytes + _currentFileBytes) * 100L + totalBytes - 1) / totalBytes);

            return (int)Math.Min(100, _filesCompleted * 100 / Math.Max(1, _catalog.Count));
        }

        private int CurrentFilePercent() =>
            _currentFileSize <= 0
                ? (_currentFileBytes > 0 ? 100 : 0)
                : (int)Math.Min(100L, _currentFileBytes * 100L / _currentFileSize);

        private void RefreshProgress(bool async = false, int? filesPercentOverride = null, int? filePercentOverride = null)
        {
            var progress = _progress;
            if (progress == null || progress.IsDisposed || _catalog.Count == 0)
                return;

            var filesPct = filesPercentOverride ?? FilesPercent();
            var filePct = filePercentOverride ?? CurrentFilePercent();
            try
            {
                void Update() => progress.ReportProgress(filesPct, filePct, _filesCompleted, _catalog.Count);
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

        private void ConfigureProgress(int fileCount)
        {
            var progress = _progress;
            if (progress == null || progress.IsDisposed)
                return;

            try
            {
                void Update() => progress.Configure(fileCount);
                if (progress.InvokeRequired)
                    progress.Invoke(Update);
                else
                    Update();
            }
            catch
            {
            }
        }

        private void SetCurrentFile(string relativePath)
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

        private void ShowFailure(string message)
        {
            Volatile.Write(ref _failureUiShown, 1);
            var progress = _progress;
            if (progress == null || progress.IsDisposed)
            {
                ShellUiHost.ShowError(IntPtr.Zero, message);
                return;
            }

            try
            {
                if (progress.InvokeRequired)
                    progress.BeginInvoke(() => progress.Fail(message));
                else
                    progress.Fail(message);
            }
            catch
            {
            }
        }

        private void CompleteProgress()
        {
            var progress = _progress;
            if (progress == null || progress.IsDisposed)
                return;

            try
            {
                if (progress.InvokeRequired)
                    progress.BeginInvoke(() => progress.Complete());
                else
                    progress.Complete();
            }
            catch
            {
            }
        }

        private void RequestCancel()
        {
            if (Interlocked.CompareExchange(ref _cancelRequested, 1, 0) != 0)
                return;

            CloseProgressUi();
        }

        private void CloseProgressUi()
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
        }

        private void ThrowIfCancelled()
        {
            if (Volatile.Read(ref _cancelRequested) != 0)
                throw new OperationCanceledException();
        }

        private void EnsureProgressShown(string initialFileLabel)
        {
            if (_progress != null)
                return;

            _progressThread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var form = new TransferProgressForm("Copying to Xbox");
                form.Configure(1);
                form.SetCurrentFile(initialFileLabel);
                form.CancelRequested += RequestCancel;
                form.CloseRequested += RequestCancel;
                form.FormClosed += (_, _) => Application.ExitThread();
                _progress = form;
                _progressReady.Set();
                form.Show();
                form.BringToFront();
                form.Activate();
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

        private void EndTransferActivity()
        {
            if (Interlocked.CompareExchange(ref _activityEnded, 1, 0) != 0)
                return;

            ShellTransferActivity.End();
        }

        private static void CreateWireDirectory(XbdmConnection conn, string wirePath)
        {
            try
            {
                conn.CreateDirectory(wirePath);
            }
            catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
            {
            }
        }

        private static void EnsureWireDirectories(XbdmConnection conn, string wirePath)
        {
            var slash = wirePath.LastIndexOf('\\');
            if (slash <= 0)
                return;

            var dir = wirePath[..slash];
            var parts = dir.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                current = $"{current}\\{parts[i]}";
                try
                {
                    CreateWireDirectory(conn, current);
                }
                catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
                {
                }
            }
        }

        public void Dispose()
        {
            try { _progressReady.Dispose(); } catch { }
        }
    }
}
