using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

internal static class XbdmProtocol
{
    public static int HResultFromStatusLine(string line)
    {
        if (line.Length < 3)
            throw XbdmException.FromHResult("Invalid XBDM status line.", XbdmHResults.CannotConnect, line);

        var nCode = (line[0] - '2') * 100 + (line[1] - '0') * 10 + (line[2] - '0');
        return line[0] == '4' ? XbdmHResults.Error(nCode - 200) : XbdmHResults.Success(nCode);
    }

    public static XbdmDirEntry ParseDmfaLine(string line, XbdmSci? sci = null)
    {
        uint attributes = 0;
        if (GetFlag(line, "directory"))
            attributes |= XbdmConstants.AttrDirectory;
        if (GetFlag(line, "readonly"))
            attributes |= XbdmConstants.AttrReadOnly;
        if (GetFlag(line, "hidden"))
            attributes |= XbdmConstants.AttrHidden;

        var changeHigh = XbdmParamParser.GetDwParam(line, "changehi");
        var changeLow = XbdmParamParser.GetDwParam(line, "changelo");
        var createHigh = XbdmParamParser.GetDwParam(line, "createhi");
        var createLow = XbdmParamParser.GetDwParam(line, "createlo");
        if (sci is not null)
        {
            XbdmTimeCorrection.CorrectFromConsole(sci, ref changeHigh, ref changeLow);
            XbdmTimeCorrection.CorrectFromConsole(sci, ref createHigh, ref createLow);
        }

        var sizeHigh = XbdmParamParser.GetDwParam(line, "sizehi");
        var sizeLow = XbdmParamParser.GetDwParam(line, "sizelo");
        var name = XbdmParamParser.GetSzParam(line, "name") ?? string.Empty;
        var changeUnix = FileTimeToUnix(changeHigh, changeLow);
        long? creationUnix = createHigh == 0 && createLow == 0
            ? null
            : FileTimeToUnix(createHigh, createLow);

        return new XbdmDirEntry(
            name,
            ((ulong)sizeHigh << 32) | sizeLow,
            attributes,
            changeUnix,
            creationUnix);
    }

    public static (ulong FreeBytes, ulong TotalBytes) ParseDiskFreeSpaceLines(IEnumerable<string> lines)
    {
        uint freeLo = 0, freeHi = 0, totalLo = 0, totalHi = 0;
        foreach (var line in lines)
        {
            freeLo = GetDwParam(line, "freetocallerlo", freeLo);
            freeHi = GetDwParam(line, "freetocallerhi", freeHi);
            totalLo = GetDwParam(line, "totalbyteslo", totalLo);
            totalHi = GetDwParam(line, "totalbyteshi", totalHi);
        }

        return (((ulong)freeHi << 32) | freeLo, ((ulong)totalHi << 32) | totalLo);
    }

    public static uint GetDwParam(string line, string key, uint defaultValue = 0)
    {
        foreach (var token in Tokenize(line))
        {
            if (!token.StartsWith(key + '=', StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = token[(key.Length + 1)..].TrimEnd(',', ';');
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (uint.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    return hex;
            }
            else if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return defaultValue;
    }

    public static (uint High, uint Low) UnixToFileTime(long unix)
    {
        if (unix == 0)
            return (0, 0);

        var ft = (ulong)unix * 10000000UL + 116444736000000000UL;
        return ((uint)(ft >> 32), (uint)ft);
    }

    public static string StripResponsePrefix(string line)
    {
        const string prefix = "200- ";
        return line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : line;
    }

    public static bool IsCommandSuccess(int hresult) =>
        XbdmHResults.IsSuccess(hresult) &&
        hresult is XbdmHResults.NoErr or XbdmHResults.Connected or XbdmHResults.Multiresponse or XbdmHResults.Binresponse or XbdmHResults.ReadyForBin;

    public static bool IsConnectWelcome(int hresult) =>
        hresult is XbdmHResults.NoErr or XbdmHResults.Connected or XbdmHResults.Multiresponse;

    public static bool TryGetQwordParam(string line, string key, out ulong value)
    {
        value = 0;
        foreach (var token in Tokenize(line))
        {
            if (!token.StartsWith(key + '=', StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = token[(key.Length + 1)..].TrimEnd(',', ';');
            if (raw.Length < 3 || !raw.StartsWith("0q", StringComparison.OrdinalIgnoreCase))
                return false;

            var hex = raw[2..];
            if (hex.Length == 0 || hex.Length > 16)
                return false;

            hex = hex.PadLeft(16, '0');
            if (!uint.TryParse(hex[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high) ||
                !uint.TryParse(hex[8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low))
            {
                return false;
            }

            value = ((ulong)high << 32) | low;
            return true;
        }

        return false;
    }

    private static bool GetFlag(string line, string key)
    {
        foreach (var token in Tokenize(line))
        {
            if (token.Equals(key, StringComparison.OrdinalIgnoreCase))
                return true;
            if (token.StartsWith(key + '=', StringComparison.OrdinalIgnoreCase))
            {
                var raw = token[(key.Length + 1)..].TrimEnd(',', ';');
                return raw is "1" or "true" or "TRUE";
            }
        }

        return false;
    }

    public static uint GetUInt(string line, string key, uint defaultValue = 0)
    {
        foreach (var token in Tokenize(line))
        {
            if (!token.StartsWith(key + '=', StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = token[(key.Length + 1)..].TrimEnd(',', ';');
            if (uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                return value;
            if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value;
        }

        return defaultValue;
    }

    public static string? GetQuotedString(string line, string key)
    {
        foreach (var token in Tokenize(line))
        {
            if (!token.StartsWith(key + "=\"", StringComparison.OrdinalIgnoreCase) || !token.EndsWith('"'))
                continue;

            return token[(key.Length + 2)..^1];
        }

        return null;
    }

    private static IEnumerable<string> Tokenize(string line)
    {
        var start = 0;
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
                inQuotes = !inQuotes;
            else if (ch == ' ' && !inQuotes)
            {
                if (i > start)
                    yield return line[start..i];
                start = i + 1;
            }
        }

        if (start < line.Length)
            yield return line[start..];
    }

    private static long FileTimeToUnix(uint high, uint low)
    {
        var ft = ((ulong)high << 32) | low;
        if (ft == 0)
            return 0;
        return (long)((ft - 116444736000000000UL) / 10000000UL);
    }

    public static uint ParseAccessPrivileges(string line)
    {
        uint access = 0;
        if (XbdmParamParser.TryGetParam(line, "read", false, true))
            access |= XbdmConstants.PrivRead;
        if (XbdmParamParser.TryGetParam(line, "write", false, true))
            access |= XbdmConstants.PrivWrite;
        if (XbdmParamParser.TryGetParam(line, "control", false, true))
            access |= XbdmConstants.PrivControl;
        if (XbdmParamParser.TryGetParam(line, "config", false, true))
            access |= XbdmConstants.PrivConfigure;
        if (XbdmParamParser.TryGetParam(line, "manage", false, true))
            access |= XbdmConstants.PrivManage;
        return access;
    }

    public static string FormatAccessPrivileges(uint access)
    {
        var builder = new StringBuilder();
        if ((access & XbdmConstants.PrivRead) != 0)
            builder.Append(" read");
        if ((access & XbdmConstants.PrivWrite) != 0)
            builder.Append(" write");
        if ((access & XbdmConstants.PrivControl) != 0)
            builder.Append(" control");
        if ((access & XbdmConstants.PrivConfigure) != 0)
            builder.Append(" config");
        if ((access & XbdmConstants.PrivManage) != 0)
            builder.Append(" manage");
        return builder.ToString();
    }
}

internal sealed class XbdmProtocolSession : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly byte[] _readBuffer = new byte[4096];
    private int _bufferOffset;
    private int _bufferCount;

    internal bool SecurityHandshakeAttempted { get; private set; }

    internal Socket Socket => _client.Client;

    private XbdmProtocolSession(TcpClient client, NetworkStream stream)
    {
        _client = client;
        _stream = stream;
    }

    internal static XbdmProtocolSession Attach(TcpClient client) =>
        new(client, client.GetStream());

    public static XbdmProtocolSession Connect(string consoleName, TimeSpan connectTimeout, XbdmConnectOptions? options = null)
    {
        options ??= new XbdmConnectOptions();
        var address = XboxNameResolver.ResolveAddress(consoleName);
        var client = new TcpClient();
        try
        {
            var connectTask = client.ConnectAsync(address, XbdmConstants.DebuggerPort);
            if (!connectTask.Wait(connectTimeout))
                throw XbdmException.FromHResult($"Could not connect to '{consoleName}'.", XbdmHResults.CannotConnect);

            var stream = client.GetStream();
            var session = new XbdmProtocolSession(client, stream);
            var welcome = session.ReceiveLine();
            var hr = XbdmProtocol.HResultFromStatusLine(welcome);

            if (!XbdmProtocol.IsConnectWelcome(hr))
                throw XbdmException.FromHResult("XBDM connect handshake failed.", hr, welcome);

            if (XbdmProtocol.TryGetQwordParam(welcome, "BOXID", out _) &&
                XbdmProtocol.TryGetQwordParam(welcome, "NONCE", out _))
            {
                session.SecurityHandshakeAttempted = true;
                XbdmSecurity.AuthenticateOnConnect(session, welcome, options, client.Client);
            }

            return session;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public (int HResult, string Line) SendCommandRaw(string command)
    {
        var payload = Encoding.ASCII.GetBytes(command + "\r\n");
        _stream.Write(payload, 0, payload.Length);

        var line = ReceiveLine();
        return (XbdmProtocol.HResultFromStatusLine(line), line);
    }

    public string SendCommand(string command)
    {
        var (hr, line) = SendCommandRaw(command);
        if (hr == XbdmHResults.Multiresponse)
            return line;

        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult($"XBDM command failed: {command}", hr, line);

        return line;
    }

    public void SendBinary(ReadOnlySpan<byte> data) => _stream.Write(data);

    public void ReceiveBinary(Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            if (_bufferOffset < _bufferCount)
            {
                var available = _bufferCount - _bufferOffset;
                var toCopy = Math.Min(available, buffer.Length - offset);
                _readBuffer.AsSpan(_bufferOffset, toCopy).CopyTo(buffer[offset..]);
                _bufferOffset += toCopy;
                offset += toCopy;
                continue;
            }

            if (!TryFillBuffer())
                throw XbdmException.FromHResult("XBDM connection lost.", XbdmHResults.ConnectionLost);
        }
    }

    internal void SetReadTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            _stream.ReadTimeout = Timeout.Infinite;
            return;
        }

        var milliseconds = (int)Math.Min(int.MaxValue, Math.Max(1, timeout.TotalMilliseconds));
        _stream.ReadTimeout = milliseconds;
    }

    private bool TryFillBuffer()
    {
        _bufferOffset = 0;
        try
        {
            _bufferCount = _stream.Read(_readBuffer, 0, _readBuffer.Length);
        }
        catch (IOException)
        {
            _bufferCount = 0;
            return false;
        }

        return _bufferCount > 0;
    }

    public uint ReceiveUInt32()
    {
        Span<byte> bytes = stackalloc byte[4];
        ReceiveBinary(bytes);
        return BitConverter.ToUInt32(bytes);
    }

    public void ReceiveStatusOrThrow(string context)
    {
        var line = ReceiveLine();
        var hr = XbdmProtocol.HResultFromStatusLine(line);
        if (!XbdmProtocol.IsCommandSuccess(hr) || hr == XbdmHResults.Multiresponse)
            throw XbdmException.FromHResult(context, hr, line);
    }

    public IReadOnlyList<string> ReadMultiresponse()
    {
        var lines = new List<string>();
        while (true)
        {
            var line = ReceiveLine();
            if (line == ".")
                break;
            lines.Add(line);
        }

        return lines;
    }

    public string ReceiveLine()
    {
        var builder = new StringBuilder();
        while (true)
        {
            if (_bufferOffset >= _bufferCount)
            {
                if (!TryFillBuffer())
                    throw XbdmException.FromHResult("XBDM connection lost.", XbdmHResults.ConnectionLost);
            }

            var ch = (char)_readBuffer[_bufferOffset++];
            if (ch == '\r')
                continue;
            if (ch == '\n')
                return builder.ToString();

            builder.Append(ch);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SendBye();
        _stream.Dispose();
        _client.Dispose();
    }

    private void SendBye()
    {
        try
        {
            if (_client.Connected)
            {
                var payload = Encoding.ASCII.GetBytes("BYE\r\n");
                _stream.Write(payload);
                _stream.Flush();
            }
        }
        catch
        {
            // Best effort; the console frees the slot when BYE is received.
        }
    }

    private bool _disposed;
}

internal static class XboxNameResolver
{
    public static IPAddress ResolveAddress(string consoleName)
    {
        if (IPAddress.TryParse(consoleName, out var parsed))
            return parsed;

        if (TryUdpNameQuery(consoleName, out var address))
            return address;

        throw XbdmException.FromHResult($"Could not resolve Xbox name '{consoleName}'.", XbdmHResults.CannotConnect);
    }

    public static bool TryResolveNameToAddress(string name, out IPAddress address) =>
        IPAddress.TryParse(name, out address!) || TryUdpNameQuery(name, out address);

    public static bool TryQueryNameAt(IPAddress consoleAddress, out string name)
    {
        name = string.Empty;
        if (consoleAddress.AddressFamily != AddressFamily.InterNetwork)
            return false;

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var packet = new byte[] { 3, 0 };
        var remote = new IPEndPoint(consoleAddress, XbdmConstants.DebuggerPort);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            socket.SendTo(packet, remote);
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                if (!socket.Poll(50_000, SelectMode.SelectRead))
                    continue;

                var responseEndpoint = (EndPoint)new IPEndPoint(IPAddress.Any, 0);
                var response = new byte[260];
                try
                {
                    var received = socket.ReceiveFrom(response, ref responseEndpoint);
                    if (received < 2 || response[0] != 2)
                        continue;

                    var length = response[1];
                    if (length <= 0 || received < 2 + length)
                        continue;

                    name = Encoding.ASCII.GetString(response, 2, length);
                    return name.Length > 0;
                }
                catch (SocketException)
                {
                    break;
                }
            }
        }

        return false;
    }

    public static uint? TryResolveAddressUInt32(string consoleName)
    {
        try
        {
            var address = ResolveAddress(consoleName);
            if (address.AddressFamily != AddressFamily.InterNetwork)
                return null;

            var bytes = address.GetAddressBytes();
            return BitConverter.ToUInt32(bytes, 0);
        }
        catch (XbdmException)
        {
            return null;
        }
    }

    private static bool TryUdpNameQuery(string name, out IPAddress address)
    {
        address = IPAddress.None;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

        var nameBytes = Encoding.ASCII.GetBytes(name);
        if (nameBytes.Length > 255)
            return false;

        var packet = new byte[2 + nameBytes.Length];
        packet[0] = 1; // name request
        packet[1] = (byte)nameBytes.Length;
        nameBytes.CopyTo(packet, 2);

        var broadcast = new IPEndPoint(IPAddress.Broadcast, XbdmConstants.DebuggerPort);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            socket.SendTo(packet, broadcast);
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                if (!socket.Poll(50_000, SelectMode.SelectRead))
                    continue;

                var remote = (EndPoint)new IPEndPoint(IPAddress.Any, 0);
                var response = new byte[260];
                try
                {
                    var received = socket.ReceiveFrom(response, ref remote);
                    if (received >= 2 && response[0] == 1)
                    {
                        address = ((IPEndPoint)remote).Address;
                        return true;
                    }
                }
                catch (SocketException)
                {
                    break;
                }
            }
        }

        return false;
    }
}
