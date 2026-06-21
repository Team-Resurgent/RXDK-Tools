namespace Rxdk.Xbdm.Managed;

internal readonly struct XbdmSessionScope : IDisposable
{
  private readonly XbdmSci? _sci;
  private readonly bool _dedicated;

  internal XbdmSessionScope(XbdmProtocolSession session, XbdmSci? sci, bool dedicated)
  {
    Session = session;
    _sci = sci;
    _dedicated = dedicated;
  }

  public XbdmProtocolSession Session { get; }

  public void Dispose()
  {
    if (_dedicated)
      Session.Dispose();
    else
      _sci?.ReleaseShared(Session);
  }
}

internal sealed class XbdmSci : IDisposable
{
  private readonly object _gate = new();
  private readonly string _consoleName;
  private readonly XbdmConnectOptions _defaultOptions;

  private XbdmProtocolSession? _sharedSession;
  private int _sharedUseCount;
  private bool _allowSharing = true;
  private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
  private TimeSpan _conversationTimeout = TimeSpan.Zero;

  internal bool GotTimeCorrection { get; set; }
  internal bool BadSysTime { get; set; }
  internal bool AddDiff { get; set; }
  internal ulong TimeDiff { get; set; }

  internal XbdmSci(string consoleName, XbdmConnectOptions? options = null)
  {
    _consoleName = consoleName;
    _defaultOptions = options ?? new XbdmConnectOptions();
  }

  internal string ConsoleName => _consoleName;

  internal void UseSharedConnection(bool enable)
  {
    lock (_gate)
    {
      if (_allowSharing == enable)
        return;

      if (!enable && _sharedSession is not null && _sharedUseCount == 0)
      {
        _sharedSession.Dispose();
        _sharedSession = null;
      }

      _allowSharing = enable;
    }
  }

  internal void SetConnectionTimeout(TimeSpan connectTimeout, TimeSpan conversationTimeout)
  {
    lock (_gate)
    {
      _connectTimeout = connectTimeout;
      _conversationTimeout = conversationTimeout;
      _sharedSession?.SetReadTimeout(conversationTimeout);
    }
  }

  internal XbdmSessionScope Acquire()
  {
    lock (_gate)
    {
      if (_allowSharing)
      {
        _sharedSession ??= XbdmProtocolSession.Connect(_consoleName, _connectTimeout, _defaultOptions);
        _sharedSession.SetReadTimeout(_conversationTimeout);
        _sharedUseCount++;
        return new XbdmSessionScope(_sharedSession, this, dedicated: false);
      }

      var dedicated = XbdmProtocolSession.Connect(_consoleName, _connectTimeout, _defaultOptions);
      dedicated.SetReadTimeout(_conversationTimeout);
      return new XbdmSessionScope(dedicated, null, dedicated: true);
    }
  }

  internal void ReleaseShared(XbdmProtocolSession session)
  {
    lock (_gate)
    {
      if (_sharedSession != session)
        return;

      if (_sharedUseCount > 0)
        _sharedUseCount--;

      if (!_allowSharing && _sharedUseCount == 0)
      {
        _sharedSession.Dispose();
        _sharedSession = null;
      }
    }
  }

  internal void OneLineCommand(string command)
  {
    using var scope = Acquire();
    var session = scope.Session;
    var (hr, _) = session.SendCommandRaw(command);
    if (hr is XbdmHResults.ReadyForBin or XbdmHResults.Multiresponse or XbdmHResults.Binresponse)
      throw XbdmException.FromHResult($"Unexpected multiline/binary response for '{command}'.", hr);
    if (!XbdmProtocol.IsCommandSuccess(hr) && hr != XbdmHResults.NoErr)
      throw XbdmException.FromHResult($"XBDM command failed: {command}", hr);
  }

  internal T WithSession<T>(Func<XbdmProtocolSession, T> action)
  {
    using var scope = Acquire();
    return action(scope.Session);
  }

  internal void WithSession(Action<XbdmProtocolSession> action) =>
    WithSession(session =>
    {
      action(session);
      return 0;
    });

  internal void InvalidateSharedConnection()
  {
    lock (_gate)
    {
      _sharedSession?.Dispose();
      _sharedSession = null;
      _sharedUseCount = 0;
      GotTimeCorrection = false;
      BadSysTime = false;
      TimeDiff = 0;
    }
  }

  public void Dispose()
  {
    lock (_gate)
    {
      _sharedSession?.Dispose();
      _sharedSession = null;
      _sharedUseCount = 0;
    }

    XbdmNotificationHub.Release(this);
  }
}

internal static class XbdmSciRegistry
{
  private static readonly object Gate = new();
  private static readonly Dictionary<string, XbdmSci> Sessions = new(StringComparer.OrdinalIgnoreCase);

  internal static XbdmSci GetOrCreate(string consoleName, XbdmConnectOptions? options = null)
  {
    lock (Gate)
    {
      if (!Sessions.TryGetValue(consoleName, out var sci))
      {
        sci = new XbdmSci(consoleName, options);
        Sessions[consoleName] = sci;
      }

      return sci;
    }
  }

    internal static void ReleaseConsole(string consoleName)
    {
        lock (Gate)
        {
            if (!Sessions.TryGetValue(consoleName, out var sci))
                return;

            sci.Dispose();
            Sessions.Remove(consoleName);
        }
    }

    internal static void DisposeAll()
  {
    lock (Gate)
    {
      foreach (var sci in Sessions.Values)
        sci.Dispose();
      Sessions.Clear();
    }
  }
}
