using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;
using Rxdk.Native;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.Tests.Parity;

internal sealed class XbdmParitySession : IDisposable
{
    private readonly NativeXbdmClient _nativeClient;
    private readonly ManagedXbdmClient _managedClient;
    private IXbdmConnection? _native;
    private IXbdmConnection? _managed;

    private XbdmParitySession(
        NativeXbdmClient nativeClient,
        ManagedXbdmClient managedClient,
        IXbdmConnection native,
        IXbdmConnection managed,
        string consoleName)
    {
        _nativeClient = nativeClient;
        _managedClient = managedClient;
        _native = native;
        _managed = managed;
        ConsoleName = consoleName;
    }

    public IXbdmConnection Native => _native ?? throw new InvalidOperationException("Parity session is not connected to the kit.");
    public IXbdmConnection Managed => _managed ?? throw new InvalidOperationException("Parity session is not connected to the kit.");
    public string ConsoleName { get; }

    public bool IsConnected => _native is not null && _managed is not null;

    public IXbdmDebugConnection NativeDebug => Native.Debug;
    public IXbdmDebugConnection ManagedDebug => Managed.Debug;

    public static bool TryCreate(
        string console,
        [NotNullWhen(true)] out XbdmParitySession? session,
        out string skipReason)
    {
        session = null;
        skipReason = string.Empty;

        if (string.IsNullOrWhiteSpace(console))
        {
            skipReason = "RXDK_TEST_CONSOLE is not set.";
            return false;
        }

        // Drop stale managed SCI sockets from a prior aborted run before opening new connections.
        XbdmSciRegistry.ReleaseConsole(console);

        var nativeClient = new NativeXbdmClient();
        var managedClient = new ManagedXbdmClient();
        nativeClient.Initialize();
        managedClient.Initialize();

        try
        {
            var native = Connect(nativeClient, console);
            var managed = Connect(managedClient, console);
            ConfigureParityTimeouts(native.Debug);
            ConfigureParityTimeouts(managed.Debug);
            session = new XbdmParitySession(nativeClient, managedClient, native, managed, console);
            return true;
        }
        catch (Exception ex)
        {
            nativeClient.Dispose();
            managedClient.Dispose();
            skipReason = $"Could not open parity session: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        ReleaseKitConnections();
        _nativeClient.Dispose();
        _managedClient.Dispose();
    }

    /// <summary>
    /// Drop parity connections so xboxdbg-bridge can own the kit's single XBDM session.
    /// </summary>
    public void ReleaseKitConnections()
    {
        _native?.Dispose();
        _managed?.Dispose();
        _native = null;
        _managed = null;
        XbdmSciRegistry.ReleaseConsole(ConsoleName);
    }

    /// <summary>
    /// Drop and reopen the native file connection so xbdm.dll relearns clock skew after setsystime.
    /// The kit accepts one XBDM session, so managed must be released first.
    /// </summary>
    public void RefreshNativeFileTimeCorrection()
    {
        ParityProgress.Phase("Refreshing native file clock skew");
        _managed?.Dispose();
        _managed = null;

        _native?.Dispose();
        _native = Connect(_nativeClient, ConsoleName);
        _ = _native.ListDrives();
        var drive = PickScratchDrive(_native);
        if (drive != default)
            _ = _native.GetFileAttributes($"{drive}:\\");

        _managed = Connect(_managedClient, ConsoleName);
        ConfigureParityTimeouts(_managed.Debug);
        if (_managed is ManagedXbdmConnection managed)
            managed.SyncConsoleClock();
    }

    /// <summary>
    /// Reboot to pending exec (STOP|WARM) and wait until the kit is ready to accept a debug launch.
    /// </summary>
    public bool RebootToPendingExec()
    {
        // WARM|WAIT (not STOP) so the launched title runs freely. STOP halts the title at its
        // entry point, which keeps D3D from ever rendering even with no debugger attached.
        ParityProgress.Phase("Rebooting to pending exec (WARM|WAIT)");
        Managed.UseSharedConnection(true);

        var pending = new ManualResetEventSlim(false);
        using (var notify = NativeDebug.OpenNotificationSession(XbdmDebugConstants.DmPersistent))
        {
            notify.Notify(XbdmDebugConstants.DmExec, (_, data) =>
            {
                if (data is int state && state == XbdmDebugConstants.DmnExecPending)
                    pending.Set();
            });

            try
            {
                NativeDebug.Reboot(XbdmDebugConstants.DmbootWarm | XbdmDebugConstants.DmbootWait);
            }
            catch (XbdmException ex)
            {
                ParityProgress.Phase($"Pending exec reboot: {ex.Message}");
            }

            if (pending.Wait(GetKitWaitTimeout()))
            {
                NativeDebug.UseSharedConnection(false);
                NativeDebug.UseSharedConnection(true);
                return true;
            }
        }

        ParityProgress.Phase("Pending exec notification not received; polling kit");
        ReleaseKitConnections();
        if (!WaitForKit(ConsoleName, "Waiting for kit after pending exec reboot"))
            return false;

        ReconnectKit();
        Managed.UseSharedConnection(true);
        return true;
    }

    /// <summary>
    /// Drop native xbdm.dll while managed performs lock/key-exchange (two clients hang the kit).
    /// Native is reconnected explicitly afterward via <see cref="ReconnectNative"/>.
    /// </summary>
    public void RunManagedExclusive(Action action)
    {
        _native?.Dispose();
        _native = null;

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

    /// <summary>
    /// Reconnect native xbdm.dll after a managed-exclusive stretch. When the kit is locked,
    /// pass the admin password so debug/file reads authenticate before opening the debug proxy.
    /// </summary>
    public void ReconnectNative(string? securePassword = null)
    {
        _native?.Dispose();
        _native = Connect(_nativeClient, ConsoleName);
        if (!string.IsNullOrEmpty(securePassword) && _native.IsSecurityEnabled())
            _native.UseSecureConnection(securePassword);
        ConfigureParityTimeouts(_native.Debug);
    }

    /// <summary>
    /// Drop and reopen the managed connection. Omit password to use PC user auth (machine name + seed).
    /// </summary>
    public void ReconnectManaged(string? adminPassword = null)
    {
        _managed?.Dispose();
        _managed = null;
        XbdmSciRegistry.ReleaseConsole(ConsoleName);

        if (string.IsNullOrWhiteSpace(adminPassword))
            _managed = Connect(_managedClient, ConsoleName);
        else
        {
            _managed = _managedClient.Connect(
                ConsoleName,
                new XbdmConnectOptions { AdminPassword = adminPassword });
        }

        ConfigureParityTimeouts(_managed.Debug);
    }

    /// <summary>
    /// Reconnect managed after a security pause (kit still locked). Uses PC user credentials.
    /// </summary>
    public void ReconnectManagedForSecurity()
    {
        ReconnectManaged();
        Managed.UseSharedConnection(true);
    }

    public void ReconnectKit()
    {
        if (IsConnected)
            return;

        if (!WaitForKit(ConsoleName, "Waiting for kit before parity reconnect"))
        {
            throw new TimeoutException(
                $"Kit did not respond within {GetKitWaitTimeout().TotalSeconds:F0}s after bridge or reboot.");
        }

        OpenFreshKitConnections();
    }

    /// <summary>Drop and reopen both sides (e.g. after the kit resets XBDM mid-title).</summary>
    public void ForceReconnectKit()
    {
        ReleaseKitConnections();
        if (!WaitForKit(ConsoleName, "Waiting for kit before parity reconnect"))
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
        ParityProgress.Phase("Reconnecting parity session to kit");
        _native = Connect(_nativeClient, ConsoleName);
        _managed = Connect(_managedClient, ConsoleName);
        ConfigureParityTimeouts(_native.Debug);
        ConfigureParityTimeouts(_managed.Debug);
        ParityProgress.Phase("Parity session reconnected");
    }

    /// <summary>Warm reboot, drop stale sockets, poll until the kit answers again.</summary>
    public bool WarmRebootToDashboard()
    {
        if (!IsConnected)
            return WaitForKit(ConsoleName, "Waiting for dashboard");

        ParityProgress.Phase("Warm reboot to dashboard");
        TryDisconnectDebugger(NativeDebug);

        try
        {
            // xbdm.dll reboot works while a title/debugger session is active; managed REBOOT can multiline-fail.
            Native.Reboot(cold: false);
        }
        catch (Exception ex)
        {
            ParityProgress.Phase($"Reboot command: {ex.Message}");
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

    /// <summary>
    /// Refresh the shared managed SCI used by both native debug proxy and managed debug.
    /// Only touch the session once — duplicate DEBUGGER/DEBUGGER DISCONNECT on one socket resets the kit.
    /// </summary>
    internal static void RecycleDebugSession(XbdmParitySession session)
    {
        TryDisconnectDebugger(session.NativeDebug);
        session.NativeDebug.UseSharedConnection(false);
        session.NativeDebug.UseSharedConnection(true);
        session.InvalidateDebugTransport();
    }

    public static TimeSpan GetKitWaitTimeout()
    {
        var env = Environment.GetEnvironmentVariable("RXDK_PARITY_REBOOT_TIMEOUT_SEC");
        var seconds = int.TryParse(env, out var parsed) && parsed > 0 ? parsed : 90;
        return TimeSpan.FromSeconds(seconds);
    }

    public static bool WaitForKit(string console, string progressLabel)
    {
        ParityProgress.Phase(progressLabel);
        Thread.Sleep(TimeSpan.FromSeconds(2));
        var probeTimeout = TimeSpan.FromSeconds(2);
        return XbdmParityWait.Until(
            () => TryProbe(console, probeTimeout),
            GetKitWaitTimeout(),
            progressLabel: progressLabel);
    }

    public static IXbdmConnection Connect(IXbdmClient client, string console)
    {
        var password = Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")
            ?? Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD");
        if (client is ManagedXbdmClient managed)
        {
            return string.IsNullOrWhiteSpace(password)
                ? managed.Connect(console)
                : managed.Connect(console, new XbdmConnectOptions { AdminPassword = password });
        }

        var connection = client.Connect(console);
        if (!string.IsNullOrWhiteSpace(password))
            connection.UseSecureConnection(password);
        return connection;
    }

    private static void ConfigureParityTimeouts(IXbdmDebugConnection debug)
    {
        var opSeconds = Environment.GetEnvironmentVariable("RXDK_PARITY_OP_TIMEOUT_SEC");
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
        return $"{drive}:\\rxdk-parity\\{id}\\{leaf}";
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

    /// <summary>
    /// Upload TriangleXDK when missing or wrong size. Must run before xboxdbg-bridge connects
    /// (the kit accepts only one active XBDM session).
    /// </summary>
    public static void EnsureTriangleXbeOnKit(IXbdmConnection connection, string localXbe, string wireXbe)
    {
        var localSize = (ulong)new FileInfo(localXbe).Length;
        try
        {
            var existing = connection.GetFileAttributes(wireXbe);
            if (existing.Size == localSize)
            {
                ParityProgress.Phase($"TriangleXDK already on kit ({localSize} bytes)");
                return;
            }
        }
        catch (XbdmException)
        {
        }

        EnsureScratchDirectory(connection, wireXbe);
        ParityProgress.Phase($"Uploading TriangleXDK to {wireXbe}");
        connection.SendFile(localXbe, wireXbe);
        ParityProgress.Phase($"Upload complete ({localSize} bytes)");
    }

    public static bool TryProbe(string console, TimeSpan connectTimeout)
    {
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
        catch
        {
            return false;
        }
        finally
        {
            managedClient.Shutdown();
        }
    }
}

internal static class ParityCompare
{
    public static ParityCheckResult Pass(
        string category,
        string name,
        string? notes = null,
        bool expectedFailure = false,
        string? backendOverride = null) =>
        new(category, name, ParityStatus.Passed, Notes: notes, ExpectedFailure: expectedFailure, BackendOverride: backendOverride);

    public static ParityCheckResult Skip(string category, string name, string reason) =>
        new(category, name, ParityStatus.Skipped, Notes: reason);

    public static ParityCheckResult Fail(
        string category,
        string name,
        string notes,
        string? nativeDetail = null,
        string? managedDetail = null) =>
        new(category, name, ParityStatus.Failed, nativeDetail, managedDetail, notes);

    public static ParityCheckResult Equal<T>(
        string category,
        string name,
        T? nativeValue,
        T? managedValue,
        string? notes = null)
    {
        if (EqualityComparer<T?>.Default.Equals(nativeValue, managedValue))
            return Pass(category, name, notes);
        return Fail(
            category,
            name,
            "Values differ.",
            FormatValue(nativeValue),
            FormatValue(managedValue));
    }

    private static string FormatValue<T>(T? value) => value?.ToString() ?? "(null)";

    public static string FormatHResult(int hresult, string? detail = null) =>
        XbdmHResults.Format(hresult, detail);

    public static string FormatSide(XbdmException? error, string? successDetail) =>
        error is not null ? FormatHResult(error.HResultCode, error.Message) : successDetail ?? "ok";

    public static string BothThrewNote(int hresult) =>
        $"Expected fail — both threw {FormatHResult(hresult)}";

    public static string ExpectedFailureNote(int hresult) => BothThrewNote(hresult);

    public static IReadOnlySet<int> AcceptInvalidCmd { get; } =
        new HashSet<int> { XbdmHResults.InvalidCmd };

    public static IReadOnlySet<int> AcceptCountUnavailable { get; } =
        new HashSet<int> { XbdmHResults.Error(18) };

    public static IReadOnlySet<int> AcceptCannotAccess { get; } =
        new HashSet<int> { XbdmHResults.Error(14) };

    private static readonly IReadOnlySet<int> RequireSuccess = new HashSet<int>();

    private static XbdmException ToSideException(Exception ex) =>
        ex as XbdmException ??
        XbdmException.FromHResult(
            ex.Message,
            ex is IOException or SocketException ? XbdmHResults.ConnectionLost : XbdmHResults.FileError);

    public static ParityCheckResult RequireSuccessOrEqual<T>(
        string category,
        string name,
        Func<T> native,
        Func<T> managed,
        Func<T, string>? format = null) =>
        BothThrowOrEqual(category, name, native, managed, format, RequireSuccess);

    public static ParityCheckResult BothThrowOrEqual<T>(
        string category,
        string name,
        Func<T> native,
        Func<T> managed,
        Func<T, string>? format = null,
        IReadOnlySet<int>? acceptedErrors = null)
    {
        format ??= v => v?.ToString() ?? "(null)";
        XbdmException? nativeError = null;
        XbdmException? managedError = null;
        T? nativeValue = default;
        T? managedValue = default;

        try
        {
            nativeValue = native();
        }
        catch (Exception ex)
        {
            nativeError = ToSideException(ex);
        }

        try
        {
            managedValue = managed();
        }
        catch (Exception ex)
        {
            managedError = ToSideException(ex);
        }

        if (nativeError is not null && managedError is not null)
        {
            acceptedErrors ??= RequireSuccess;
            if (nativeError.HResultCode == managedError.HResultCode)
            {
                if (!acceptedErrors.Contains(nativeError.HResultCode))
                {
                    return Fail(
                        category,
                        name,
                        $"Both threw {FormatHResult(nativeError.HResultCode)} but this check requires success.",
                        FormatHResult(nativeError.HResultCode, nativeError.Message),
                        FormatHResult(managedError.HResultCode, managedError.Message));
                }

                return Pass(category, name, BothThrewNote(nativeError.HResultCode), expectedFailure: true);
            }

            return Fail(
                category,
                name,
                "Both threw but HRESULT differs.",
                FormatHResult(nativeError.HResultCode, nativeError.Message),
                FormatHResult(managedError.HResultCode, managedError.Message));
        }

        if (nativeError is not null || managedError is not null)
        {
            return Fail(
                category,
                name,
                "Only one side threw.",
                FormatSide(nativeError, format(nativeValue!)),
                FormatSide(managedError, format(managedValue!)));
        }

        if (nativeValue is null && managedValue is null)
            return Pass(category, name);

        if (nativeValue is null || managedValue is null)
            return Fail(category, name, "Null mismatch.", format(nativeValue!), format(managedValue!));

        return Equal(category, name, nativeValue, managedValue);
    }

    public static ParityCheckResult BothAction(
        string category,
        string name,
        Action native,
        Action managed,
        IReadOnlySet<int>? acceptedErrors = null)
    {
        XbdmException? nativeError = null;
        XbdmException? managedError = null;

        try
        {
            native();
        }
        catch (Exception ex)
        {
            nativeError = ToSideException(ex);
        }

        try
        {
            managed();
        }
        catch (Exception ex)
        {
            managedError = ToSideException(ex);
        }

        if (nativeError is not null && managedError is not null)
        {
            acceptedErrors ??= RequireSuccess;
            if (nativeError.HResultCode == managedError.HResultCode)
            {
                if (!acceptedErrors.Contains(nativeError.HResultCode))
                {
                    return Fail(
                        category,
                        name,
                        $"Both threw {FormatHResult(nativeError.HResultCode)} but this check requires success.",
                        FormatHResult(nativeError.HResultCode, nativeError.Message),
                        FormatHResult(managedError.HResultCode, managedError.Message));
                }

                return Pass(category, name, BothThrewNote(nativeError.HResultCode), expectedFailure: true);
            }

            return Fail(
                category,
                name,
                "Both threw but HRESULT differs.",
                FormatHResult(nativeError.HResultCode, nativeError.Message),
                FormatHResult(managedError.HResultCode, managedError.Message));
        }

        if (nativeError is not null || managedError is not null)
        {
            return Fail(
                category,
                name,
                "Only one side threw.",
                FormatSide(nativeError, "ok"),
                FormatSide(managedError, "ok"));
        }

        return Pass(category, name);
    }
}
