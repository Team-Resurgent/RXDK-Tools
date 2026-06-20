namespace Rxdk.Xbdm.Managed;

/// <summary>
/// Incrementally reads a single Xbox file over an active XBDM session without writing to disk.
/// The session must not be used for other commands until this receiver is completed or abandoned.
/// </summary>
public sealed class XbdmFileReceiver : IDisposable
{
    private readonly XbdmProtocolSession _session;
    private readonly string _wirePath;
    private ulong _remaining;
    private bool _started;
    private bool _completed;
    private bool _disposed;

    internal XbdmFileReceiver(XbdmProtocolSession session, string wirePath)
    {
        _session = session;
        _wirePath = wirePath;
    }

    public ulong TotalSize { get; private set; }
    public ulong Position { get; private set; }
    public bool IsComplete => _completed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;

        var (hr, _) = _session.SendCommandRaw($"GETFILE NAME=\"{_wirePath}\"");
        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult($"Could not receive '{_wirePath}'.", hr);

        TotalSize = _session.ReceiveUInt32();
        _remaining = TotalSize;
        _started = true;
    }

    public int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
            Start();

        if (_remaining == 0)
        {
            _completed = true;
            return 0;
        }

        var count = (int)Math.Min(_remaining, (ulong)buffer.Length);
        _session.ReceiveBinary(buffer[..count]);
        _remaining -= (ulong)count;
        Position += (ulong)count;
        if (_remaining == 0)
            _completed = true;

        return count;
    }

    public void Skip(ulong bytes)
    {
        if (bytes == 0)
            return;

        Span<byte> scratch = stackalloc byte[8192];
        while (bytes > 0)
        {
            var chunk = (int)Math.Min(bytes, (ulong)scratch.Length);
            var read = Read(scratch[..chunk]);
            if (read == 0)
                break;

            bytes -= (ulong)read;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_started && !_completed && _remaining > 0)
        {
            try
            {
                Skip(_remaining);
            }
            catch
            {
                // Leave the session for the connection owner to discard.
            }
        }
    }
}
