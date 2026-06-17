using System.Net;

using System.Net.Sockets;

using Rxdk.Xbdm;



namespace Rxdk.Xbdm.Managed;



internal sealed class XbdmNotificationHub

{

    private static readonly object RegistryGate = new();

    private static readonly Dictionary<XbdmSci, XbdmNotificationHub> Hubs = new();



    private readonly XbdmSci _sci;

    private readonly object _gate = new();

    private TcpListener? _listener;

    private int _listenPort;

    private XbdmProtocolSession? _notifySession;

    private CancellationTokenSource? _cts;

    private int _sessionCount;



    private XbdmNotificationHub(XbdmSci sci) => _sci = sci;



    internal static XbdmNotificationHub GetOrCreate(XbdmSci sci)

    {

        lock (RegistryGate)

        {

            if (!Hubs.TryGetValue(sci, out var hub))

            {

                hub = new XbdmNotificationHub(sci);

                Hubs[sci] = hub;

            }



            return hub;

        }

    }

    internal static void Release(XbdmSci sci)
    {
        lock (RegistryGate)
        {
            if (!Hubs.TryGetValue(sci, out var hub))
                return;

            hub.StopNotifier();
            Hubs.Remove(sci);
        }
    }

    internal IXbdmNotificationSession OpenSession(uint flags)

    {

        lock (_gate)

        {

            EnsureNotifier();

            _sessionCount++;

            return new ManagedXbdmNotificationSession(flags, this);

        }

    }



    internal void Unregister(ManagedXbdmNotificationSession session)

    {

        lock (_gate)

        {

            session.ClearHandlers();

            if (_sessionCount > 0)

                _sessionCount--;

            if (_sessionCount == 0)

                StopNotifier();

        }

    }



    private void EnsureNotifier()

    {

        if (_listener is not null)

            return;



        _listener = new TcpListener(IPAddress.Any, 0);

        _listener.Start();

        _listenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _cts = new CancellationTokenSource();



        _sci.WithSession(session => session.SendCommand($"NOTIFYAT PORT={_listenPort}"));



        _ = Task.Run(() => AcceptLoop(_cts.Token));

    }



    private void StopNotifier()

    {

        _cts?.Cancel();

        _cts?.Dispose();

        _cts = null;



        _notifySession?.Dispose();

        _notifySession = null;



        if (_listener is not null)

        {

            try

            {

                _sci.WithSession(session => session.SendCommand($"NOTIFYAT PORT={_listenPort} DROP"));

            }

            catch

            {

                // Best effort while shutting down notifications.

            }



            _listener.Stop();

            _listener = null;

        }



        XbdmNotificationParser.ResetExecState();

        XbdmAssertBuffer.Reset();

    }



    private void AcceptLoop(CancellationToken token)

    {

        while (!token.IsCancellationRequested)

        {

            TcpClient client;

            try

            {

                client = _listener!.AcceptTcpClient();

            }

            catch (ObjectDisposedException)

            {

                break;

            }

            catch when (token.IsCancellationRequested)

            {

                break;

            }

            catch

            {

                continue;

            }



            _notifySession?.Dispose();

            _notifySession = XbdmProtocolSession.Attach(client);

            while (!token.IsCancellationRequested)

            {

                if (!ReadNotifications(_notifySession, token))

                    break;

            }

        }

    }



    private bool ReadNotifications(XbdmProtocolSession session, CancellationToken token)

    {

        while (!token.IsCancellationRequested)

        {

            string line;

            try

            {

                line = session.ReceiveLine();

            }

            catch

            {

                return false;

            }



            if (XbdmNotificationParser.TryGetExternalPrefix(line, out var prefix))

            {

                ManagedXbdmNotificationSession.DispatchExternal(prefix, line);

                continue;

            }



            if (!XbdmNotificationParser.TryHandleNotification(line, out var dispatches))

                continue;



            foreach (var dispatch in dispatches)

            {

                if ((dispatch.Code & XbdmDebugConstants.NotificationMask) == XbdmDebugConstants.DmExec &&

                    dispatch.Data is int execState &&

                    execState == XbdmDebugConstants.DmnExecReboot)

                {

                    _sci.InvalidateSharedConnection();

                }



                ManagedXbdmNotificationSession.DispatchAll(dispatch.Code, dispatch.Data);

            }

        }



        return true;

    }

}



internal sealed class ManagedXbdmNotificationSession : IXbdmNotificationSession

{

    private const int MaxHandlersPerNotification = 16;

    private const int MaxExtHandlers = 8;



    private static readonly object SessionsGate = new();

    private static readonly List<ManagedXbdmNotificationSession> ActiveSessions = new();



    private readonly XbdmNotificationHub _hub;

    private readonly Dictionary<uint, List<XbdmNotifyHandler>> _handlers = new();

    private readonly List<ExtHandler> _extHandlers = new();

    private bool _disposed;



    private sealed record ExtHandler(string Name, XbdmExtNotifyHandler Handler);



    internal ManagedXbdmNotificationSession(uint flags, XbdmNotificationHub hub)

    {

        Flags = flags;

        _hub = hub;

        lock (SessionsGate)

            ActiveSessions.Add(this);

    }



    public uint Flags { get; }



    public void Notify(uint notificationMask, XbdmNotifyHandler handler)

    {

        ObjectDisposedException.ThrowIf(_disposed, this);

        var code = notificationMask & XbdmDebugConstants.NotificationMask;



        if (code == XbdmDebugConstants.DmNone)

        {

            if (handler is null)

            {

                _handlers.Clear();

                return;

            }



            foreach (var handlers in _handlers.Values)
                handlers.RemoveAll(h => h == handler);

            return;

        }



        if (handler is null || code > XbdmDebugConstants.DmNotifyMax)

            throw new ArgumentException("Invalid notification registration.", nameof(notificationMask));



        if (!_handlers.TryGetValue(code, out var list))

        {

            list = new List<XbdmNotifyHandler>();

            _handlers[code] = list;

        }



        if (list.Count >= MaxHandlersPerNotification)

            throw new OutOfMemoryException("Too many notification handlers registered.");



        list.Add(handler);

    }



    public void RegisterNotificationProcessor(string type, XbdmExtNotifyHandler handler)

    {

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (handler is null)

        {

            if (string.IsNullOrEmpty(type))

                throw new ArgumentException("Extended notification type is required.", nameof(type));



            _extHandlers.RemoveAll(entry => entry.Name.Equals(type, StringComparison.OrdinalIgnoreCase));

            return;

        }



        if (string.IsNullOrEmpty(type) || type.Length >= 64)

            throw new ArgumentException("Extended notification type is invalid.", nameof(type));



        if (_extHandlers.Count >= MaxExtHandlers)

            throw new OutOfMemoryException("Too many extended notification processors registered.");



        _extHandlers.Add(new ExtHandler(type, handler));

    }



    internal void ClearHandlers()

    {

        _handlers.Clear();

        _extHandlers.Clear();

    }



    internal static void DispatchAll(uint code, object? data)

    {

        var notification = code & XbdmDebugConstants.NotificationMask;

        XbdmNotifyHandler[] snapshot;

        lock (SessionsGate)

        {

            snapshot = ActiveSessions

                .SelectMany(s => s._handlers.TryGetValue(notification, out var list)

                    ? list

                    : Enumerable.Empty<XbdmNotifyHandler>())

                .ToArray();

        }



        foreach (var handler in snapshot)

            handler(code, data);

    }



    internal static void DispatchExternal(string prefix, string line)

    {

        XbdmExtNotifyHandler[] snapshot;

        lock (SessionsGate)

        {

            snapshot = ActiveSessions

                .SelectMany(s => s._extHandlers.Where(e => e.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase))

                    .Select(e => e.Handler))

                .ToArray();

        }



        foreach (var handler in snapshot)

            handler(line);

    }



    public void Dispose()

    {

        if (_disposed)

            return;

        _disposed = true;

        _hub.Unregister(this);

        lock (SessionsGate)

            ActiveSessions.Remove(this);

    }

}


