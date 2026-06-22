using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.Tests.Hardware;

internal sealed class XbdmKitSession : IDisposable
{
    private readonly ManagedXbdmClient _client;
    private IXbdmConnection? _connection;

    private XbdmKitSession(ManagedXbdmClient client, IXbdmConnection connection, string consoleName)
    {
        _client = client;
        _connection = connection;
        ConsoleName = consoleName;
    }

    public IXbdmConnection Managed => _connection ?? throw new InvalidOperationException("Session is not connected to the kit.");
    public IXbdmDebugConnection ManagedDebug => Managed.Debug;
    public string ConsoleName { get; }

    public bool IsConnected => _connection is not null;

    public static bool TryCreate(
        string console,
        [NotNullWhen(true)] out XbdmKitSession? session,
        out string skipReason)
    {
        session = null;
        skipReason = string.Empty;

        if (string.IsNullOrWhiteSpace(console))
        {
            skipReason = "RXDK_TEST_CONSOLE is not set.";
            return false;
        }

        XbdmSciRegistry.ReleaseConsole(console);

        var client = new ManagedXbdmClient();
        client.Initialize();

        try
        {
            var connection = Connect(client, console);
            ConfigureKitTimeouts(connection.Debug);
            session = new XbdmKitSession(client, connection, console);
            return true;
        }
        catch (Exception ex)
        {
            client.Dispose();
            skipReason = $"Could not open session: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        ReleaseKitConnections();
        _client.Dispose();
    }

    public void ReleaseKitConnections()
    {
        _connection?.Dispose();
        _connection = null;
        XbdmSciRegistry.ReleaseConsole(ConsoleName);
    }

    public void RunManagedExclusive(Action action)
    {
        Managed.UseSharedConnection(true);
        try
        {
            action();
        }
        finally
        {
            Managed.UseSharedConnection(false);
        }
    }

    public void ReconnectManaged(string? adminPassword = null)
    {
        _connection?.Dispose();
        _connection = null;
        XbdmSciRegistry.ReleaseConsole(ConsoleName);

        if (string.IsNullOrWhiteSpace(adminPassword))
            _connection = Connect(_client, ConsoleName);
        else
        {
            _connection = _client.Connect(
                ConsoleName,
                new XbdmConnectOptions { AdminPassword = adminPassword });
        }

        ConfigureKitTimeouts(ManagedDebug);
    }

    public void ReconnectManagedForSecurity()
    {
        ReconnectManaged();
        Managed.UseSharedConnection(true);
    }

    public void ReconnectKit()
    {
        if (IsConnected)
            return;

        if (!WaitForKit(ConsoleName, "Waiting for kit before reconnect"))
        {
            throw new TimeoutException(
                $"Kit did not respond within {GetKitWaitTimeout().TotalSeconds:F0}s after bridge or reboot.");
        }

        OpenFreshKitConnections();
    }

    public void ForceReconnectKit()
    {
        ReleaseKitConnections();
        if (!WaitForKit(ConsoleName, "Waiting for kit before reconnect"))
        {
            throw new TimeoutException(
                $"Kit did not respond within {GetKitWaitTimeout().TotalSeconds:F0}s after transport loss.");
        }

        OpenFreshKitConnections();
    }

    internal void InvalidateDebugTransport() =>
        XbdmSciRegistry.GetOrCreate(ConsoleName).InvalidateSharedConnection();

    private void OpenFreshKitConnections()
    {
        KitTestProgress.Phase("Reconnecting session to kit");
        _connection = Connect(_client, ConsoleName);
        ConfigureKitTimeouts(ManagedDebug);
        KitTestProgress.Phase("Session reconnected");
    }

    public bool RebootToPendingExec()
    {
        KitTestProgress.Phase("Rebooting to pending exec (WARM|WAIT)");
        Managed.UseSharedConnection(true);

        var pending = new ManualResetEventSlim(false);
        var gotPending = false;
        using (var notify = ManagedDebug.OpenNotificationSession(XbdmDebugConstants.DmPersistent))
        {
            notify.Notify(XbdmDebugConstants.DmExec, (_, data) =>
            {
                if (data is int state && state == XbdmDebugConstants.DmnExecPending)
                    pending.Set();
            });

            try
            {
                ManagedDebug.Reboot(XbdmDebugConstants.DmbootWarm | XbdmDebugConstants.DmbootWait);
            }
            catch (XbdmException ex)
            {
                KitTestProgress.Phase($"Pending exec reboot: {ex.Message}");
            }

            gotPending = pending.Wait(GetKitWaitTimeout());
        }

        // WARM reboot drops the transport even when DmExec pending arrives on the old socket.
        ReleaseKitConnections();

        if (gotPending)
            KitTestProgress.Phase("Pending exec notification received; reconnecting");
        else
            KitTestProgress.Phase("Pending exec notification not received; polling kit");

        if (!WaitForKit(ConsoleName, "Waiting for kit after pending exec reboot"))
            return false;

        ReconnectKit();
        Managed.UseSharedConnection(true);
        return true;
    }

    public bool WarmRebootToDashboard()
    {
        if (!IsConnected)
            return WaitForKit(ConsoleName, "Waiting for dashboard");

        KitTestProgress.Phase("Warm reboot to dashboard");
        TryDisconnectDebugger(ManagedDebug);

        try
        {
            Managed.Reboot(cold: false);
        }
        catch (Exception ex)
        {
            KitTestProgress.Phase($"Reboot command: {ex.Message}");
        }

        ReleaseKitConnections();
        return WaitForKit(ConsoleName, "Waiting for dashboard after reboot");
    }

    internal static void TryDisconnectDebugger(IXbdmDebugConnection debug)
    {
        try
        {
            debug.ConnectDebugger(false);
        }
        catch (XbdmException)
        {
        }
        catch (IOException)
        {
        }
    }

    internal static void RecycleDebugSession(XbdmKitSession session)
    {
        TryDisconnectDebugger(session.ManagedDebug);
        session.ManagedDebug.UseSharedConnection(false);
        session.ManagedDebug.UseSharedConnection(true);
        session.InvalidateDebugTransport();
    }

    public static TimeSpan GetKitWaitTimeout()
    {
        var env = KitTestConfig.Env("REBOOT_TIMEOUT_SEC");
        var seconds = int.TryParse(env, out var parsed) && parsed > 0 ? parsed : 90;
        return TimeSpan.FromSeconds(seconds);
    }

    public static bool WaitForKit(string console, string progressLabel)
    {
        KitTestProgress.Phase(progressLabel);
        Thread.Sleep(TimeSpan.FromSeconds(2));
        var probeTimeout = TimeSpan.FromSeconds(2);
        return XbdmKitWait.Until(
            () => TryProbe(console, probeTimeout),
            GetKitWaitTimeout(),
            progressLabel: progressLabel);
    }

    public static IXbdmConnection Connect(ManagedXbdmClient client, string console)
    {
        var password = Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")
            ?? Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD");
        return string.IsNullOrWhiteSpace(password)
            ? client.Connect(console)
            : client.Connect(console, new XbdmConnectOptions { AdminPassword = password });
    }

    private static void ConfigureKitTimeouts(IXbdmDebugConnection debug)
    {
        var opSeconds = KitTestConfig.Env("OP_TIMEOUT_SEC");
        var opTimeout = int.TryParse(opSeconds, out var parsed) && parsed > 0
            ? TimeSpan.FromSeconds(parsed)
            : TimeSpan.FromSeconds(30);
        debug.SetConnectionTimeout(TimeSpan.FromSeconds(15), opTimeout);
    }

    public static char PickScratchDrive(IXbdmConnection connection)
    {
        foreach (var drive in connection.ListDrives())
        {
            if (drive is 'E' or 'T' or 'D')
                return drive;
        }

        return connection.ListDrives().FirstOrDefault();
    }

    public static string NewScratchWirePath(IXbdmConnection connection, string leaf)
    {
        var drive = PickScratchDrive(connection);
        if (drive == default)
            throw new InvalidOperationException("No Xbox drives available for scratch tests.");
        var id = Guid.NewGuid().ToString("N");
        return $"{drive}:\\rxdk-kit-test\\{id}\\{leaf}";
    }

    public static void EnsureScratchDirectory(IXbdmConnection connection, string fileWirePath)
    {
        var dir = fileWirePath[..fileWirePath.LastIndexOf('\\')];
        EnsureDirectoryPath(connection, dir);
    }

    private static void EnsureDirectoryPath(IXbdmConnection connection, string wireDir)
    {
        var slash = wireDir.IndexOf('\\');
        if (slash < 0)
            return;

        var current = wireDir[..(slash + 1)];
        var rest = wireDir[(slash + 1)..];
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current += part;
            TryCreateDirectory(connection, current);
            current += "\\";
        }
    }

    private static void TryCreateDirectory(IXbdmConnection connection, string wirePath)
    {
        try
        {
            connection.CreateDirectory(wirePath);
        }
        catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
        {
        }
    }

    public static string TestFilesDirectory()
    {
        var fromOutput = Path.Combine(AppContext.BaseDirectory, "TestFiles");
        if (Directory.Exists(fromOutput))
            return fromOutput;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestFiles"));
    }

    public static void EnsureTriangleXbeOnKit(IXbdmConnection connection, string localXbe, string wireXbe)
    {
        var localSize = (ulong)new FileInfo(localXbe).Length;
        try
        {
            var existing = connection.GetFileAttributes(wireXbe);
            if (existing.Size == localSize)
            {
                KitTestProgress.Phase($"TriangleXDK already on kit ({localSize} bytes)");
                return;
            }
        }
        catch (XbdmException)
        {
        }

        EnsureScratchDirectory(connection, wireXbe);
        KitTestProgress.Phase($"Uploading TriangleXDK to {wireXbe}");
        connection.SendFile(localXbe, wireXbe);
        KitTestProgress.Phase($"Upload complete ({localSize} bytes)");
    }

    public static bool TryProbe(string console, TimeSpan connectTimeout) =>
        TryProbe(console, connectTimeout, out _);

    public static bool TryProbe(string console, TimeSpan connectTimeout, out string? error)
    {
        error = null;
        using var managedClient = new ManagedXbdmClient();
        managedClient.Initialize();
        try
        {
            var password = Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")
                ?? Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD");
            var options = new XbdmConnectOptions
            {
                AdminPassword = password,
                ConnectTimeout = connectTimeout,
            };
            using var connection = managedClient.Connect(console, options);
            _ = connection.ListDrives();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            managedClient.Shutdown();
        }
    }
}

internal static class KitCheck
{
    public static KitCheckResult Pass(
        string category,
        string name,
        string? notes = null,
        bool expectedFailure = false) =>
        new(category, name, KitCheckStatus.Passed, Notes: notes, ExpectedFailure: expectedFailure);

    public static KitCheckResult Skip(string category, string name, string reason) =>
        new(category, name, KitCheckStatus.Skipped, Notes: reason);

    public static KitCheckResult Fail(string category, string name, string notes, string? detail = null) =>
        new(category, name, KitCheckStatus.Failed, ManagedDetail: detail, Notes: notes);

    public static KitCheckResult ManagedCheck(string category, string name, Action action, string? successNote = null)
    {
        try
        {
            action();
            return Pass(category, name, successNote);
        }
        catch (Exception ex)
        {
            return Fail(category, name, ex.Message, FormatSide(ToSideException(ex), null));
        }
    }

    public static KitCheckResult ManagedCheck<T>(
        string category,
        string name,
        Func<T> action,
        Func<T, string>? format = null)
    {
        try
        {
            var value = action();
            return Pass(category, name, format?.Invoke(value) ?? value?.ToString());
        }
        catch (Exception ex)
        {
            return Fail(category, name, ex.Message, FormatSide(ToSideException(ex), null));
        }
    }

    public static string FormatHResult(int hresult, string? detail = null) =>
        XbdmHResults.Format(hresult, detail);

    public static string FormatSide(XbdmException? error, string? successDetail) =>
        error is not null ? FormatHResult(error.HResultCode, error.Message) : successDetail ?? "ok";

    public static string ExpectedFailureNote(int hresult) =>
        $"Expected fail — {FormatHResult(hresult)}";

    public static IReadOnlySet<int> AcceptInvalidCmd { get; } =
        new HashSet<int> { XbdmHResults.InvalidCmd };

    public static IReadOnlySet<int> AcceptCountUnavailable { get; } =
        new HashSet<int> { XbdmHResults.Error(18) };

    public static IReadOnlySet<int> AcceptCannotAccess { get; } =
        new HashSet<int> { XbdmHResults.Error(14) };

    private static XbdmException ToSideException(Exception ex) =>
        ex as XbdmException ??
        XbdmException.FromHResult(
            ex.Message,
            ex is IOException or SocketException ? XbdmHResults.ConnectionLost : XbdmHResults.FileError);

    public static KitCheckResult ManagedAction(
        string category,
        string name,
        Action action,
        IReadOnlySet<int>? acceptedErrors = null)
    {
        try
        {
            action();
            return Pass(category, name);
        }
        catch (Exception ex)
        {
            var xbdm = ToSideException(ex);
            acceptedErrors ??= new HashSet<int>();
            if (acceptedErrors.Contains(xbdm.HResultCode))
                return Pass(category, name, ExpectedFailureNote(xbdm.HResultCode), expectedFailure: true);

            return Fail(category, name, ex.Message, FormatHResult(xbdm.HResultCode, ex.Message));
        }
    }
}
