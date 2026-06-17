using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

internal sealed class ManagedXbdmDebugConnection : IXbdmDebugConnection
{
    private readonly XbdmSci _sci;
    private readonly XbdmNotificationHub _notifications;

    internal ManagedXbdmDebugConnection(XbdmSci sci)
    {
        _sci = sci;
        _notifications = XbdmNotificationHub.GetOrCreate(sci);
    }

    public void UseSharedConnection(bool enable) => _sci.UseSharedConnection(enable);

    public void SetConnectionTimeout(TimeSpan connectTimeout, TimeSpan conversationTimeout) =>
        _sci.SetConnectionTimeout(connectTimeout, conversationTimeout);

    public IXbdmNotificationSession OpenNotificationSession(uint flags) =>
        _notifications.OpenSession(flags);

    public void CloseNotificationSession(IXbdmNotificationSession session) => session.Dispose();

    public void SetBreakpoint(nuint address) =>
        _sci.OneLineCommand($"BREAK ADDR=0x{(uint)address:x8}");

    public void RemoveBreakpoint(nuint address) =>
        _sci.OneLineCommand($"BREAK ADDR=0x{(uint)address:x8} CLEAR");

    public void SetInitialBreakpoint() => _sci.OneLineCommand("BREAK START");

    public void SetDataBreakpoint(nuint address, uint breakType, uint size)
    {
        if (breakType != XbdmDebugConstants.DmbreakNone && size is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(size));

        var type = breakType switch
        {
            XbdmDebugConstants.DmbreakNone => "WRITE",
            XbdmDebugConstants.DmbreakWrite => "WRITE",
            XbdmDebugConstants.DmbreakReadWrite => "READ",
            XbdmDebugConstants.DmbreakExecute => "EXECUTE",
            _ => throw new ArgumentOutOfRangeException(nameof(breakType)),
        };
        var clear = breakType == XbdmDebugConstants.DmbreakNone ? " CLEAR" : string.Empty;
        _sci.OneLineCommand($"BREAK {type}=0x{(uint)address:x8} SIZE={size}{clear}");
    }

    public uint GetBreakpointType(nuint address) =>
        _sci.WithSession(session =>
        {
            var (hr, line) = session.SendCommandRaw($"ISBREAK ADDR=0x{(uint)address:x8}");
            if (hr != XbdmHResults.NoErr)
                throw XbdmException.FromHResult("Could not query breakpoint.", hr, line);
            return XbdmParamParser.GetDwParam(line, "type");
        });

    public void Go() => _sci.OneLineCommand("GO");
    public void Stop() => _sci.OneLineCommand("STOP");
    public void HaltThread(uint threadId) => _sci.OneLineCommand($"HALT THREAD={threadId}");

    public void ContinueThread(uint threadId, bool exception) =>
        _sci.OneLineCommand($"CONTINUE THREAD={threadId}{(exception ? " EXCEPTION" : string.Empty)}");

    public void SetupFunctionCall(uint threadId) => _sci.OneLineCommand($"FUNCCALL THREAD={threadId}");
    public void ConnectDebugger(bool connect) => _sci.OneLineCommand($"DEBUGGER {(connect ? "CONNECT" : "DISCONNECT")}");

    public void StopOn(uint stopFlags, bool stop)
    {
        var builder = new StringBuilder(stop ? "STOPON" : "NOSTOPON");
        if ((stopFlags & XbdmDebugConstants.DmstopCreateThread) != 0)
            builder.Append(" CREATETHREAD");
        if ((stopFlags & XbdmDebugConstants.DmstopFce) != 0)
            builder.Append(" FCE");
        if ((stopFlags & XbdmDebugConstants.DmstopDebugStr) != 0)
            builder.Append(" DEBUGSTR");
        _sci.OneLineCommand(builder.ToString());
    }

    public void Reboot(uint flags)
    {
        var builder = new StringBuilder("REBOOT");
        if ((flags & XbdmDebugConstants.DmbootStop) != 0)
            builder.Append(" STOP");
        else if ((flags & XbdmDebugConstants.DmbootWait) != 0)
            builder.Append(" WAIT");
        if ((flags & XbdmDebugConstants.DmbootWarm) != 0)
            builder.Append(" WARM");
        if ((flags & XbdmDebugConstants.DmbootNoDebug) != 0)
            builder.Append(" NODEBUG");
        _sci.OneLineCommand(builder.ToString());
    }

    public int GetMemory(nuint address, Span<byte> buffer)
    {
        var temp = new byte[buffer.Length];
        var read = GetMemoryArray(address, temp);
        temp.AsSpan(0, read).CopyTo(buffer);
        return read;
    }

    private int GetMemoryArray(nuint address, byte[] buffer) =>
        _sci.WithSession(session =>
        {
            var (hr, _) = session.SendCommandRaw(
                $"GETMEM ADDR=0x{(uint)address:x8} LENGTH=0x{buffer.Length:x}");
            if (hr != XbdmHResults.Multiresponse)
                throw XbdmException.FromHResult("GETMEM failed.", hr);

            var offset = 0;
            while (offset < buffer.Length)
            {
                var line = session.ReceiveLine();
                if (line == ".")
                    break;

                foreach (var pair in SplitHexPairs(line))
                {
                    if (pair == "?")
                        return offset;
                    if (offset >= buffer.Length)
                        break;
                    buffer[offset++] = Convert.ToByte(pair, 16);
                }
            }

            return offset;
        });

    public int SetMemory(nuint address, ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        return SetMemoryArray(address, bytes);
    }

    private int SetMemoryArray(nuint address, byte[] data) =>
        _sci.WithSession(session =>
        {
            var sent = 0;
            var offset = 0;
            while (offset < data.Length)
            {
                var chunk = Math.Min(64, data.Length - offset);
                var builder = new StringBuilder($"SETMEM ADDR=0x{(uint)(address + (uint)offset):x8} ");
                for (var i = 0; i < chunk; i++)
                    builder.Append(data[offset + i].ToString("x2", CultureInfo.InvariantCulture));
                var (hr, line) = session.SendCommandRaw(builder.ToString());
                if (hr is not XbdmHResults.NoErr and not XbdmHResults.MemUnmapped)
                    throw XbdmException.FromHResult("SETMEM failed.", hr, line);
                var chunkSent = ParseSetMemCount(line);
                if (chunkSent <= 0)
                    break;
                offset += chunkSent;
                sent += chunkSent;
            }

            return sent;
        });

    public IReadOnlyList<uint> GetThreadList() =>
        _sci.WithSession(session =>
        {
            var (hr, _) = session.SendCommandRaw("THREADS");
            if (hr != XbdmHResults.Multiresponse)
                throw XbdmException.FromHResult("THREADS failed.", hr);

            var threads = new List<uint>();
            while (true)
            {
                var line = session.ReceiveLine();
                if (line == ".")
                    break;
                if (uint.TryParse(line, out var threadId))
                    threads.Add(threadId);
            }

            return threads;
        });

    public void GetThreadContext(uint threadId, ref XbdmContext context)
    {
        var flags = context.ContextFlags;
        var updated = context;
        _sci.WithSession(session =>
        {
            updated.ContextFlags = 0;
            var command = new StringBuilder($"GETCONTEXT THREAD={threadId}");
            var wantControl = (flags & XbdmDebugConstants.ContextControl) == XbdmDebugConstants.ContextControl;
            var wantInteger = (flags & XbdmDebugConstants.ContextInteger) == XbdmDebugConstants.ContextInteger;
            var wantFp = (flags & XbdmDebugConstants.ContextFloatingPoint) == XbdmDebugConstants.ContextFloatingPoint;
            var wantExtended = (flags & XbdmDebugConstants.ContextExtendedRegisters) ==
                               XbdmDebugConstants.ContextExtendedRegisters;
            var wantExtCtx = wantFp || wantExtended;
            uint cr0Npx = 0;

            if (wantControl)
                command.Append(" CONTROL");
            if (wantInteger)
                command.Append(" INT");
            if (wantFp)
                command.Append(" FP");

            if (wantControl || wantInteger || wantFp)
            {
                var (hr, _) = session.SendCommandRaw(command.ToString());
                if (hr != XbdmHResults.Multiresponse)
                    throw XbdmException.FromHResult("GETCONTEXT failed.", hr);

                while (true)
                {
                    var line = session.ReceiveLine();
                    if (line == ".")
                        break;
                    ApplyContextLine(ref updated, line, ref cr0Npx);
                }
            }

            if (wantExtCtx)
            {
                if (XbdmExtendedContext.TryGetExtendedContext(session, threadId, out var xfs))
                {
                    XbdmExtendedContext.ApplyExtendedToContext(ref updated, xfs, cr0Npx);
                }
                else if (!wantControl && !wantInteger && !wantFp)
                {
                    updated.ContextFlags = 0;
                }
                else if ((updated.ContextFlags & (XbdmDebugConstants.ContextControl | XbdmDebugConstants.ContextInteger)) == 0)
                {
                    updated.ContextFlags &= XbdmDebugConstants.ContextFull;
                }
            }
        });
        context = updated;
    }

    public void SetThreadContext(uint threadId, ref XbdmContext context)
    {
        var updated = context;
        _sci.WithSession(session => XbdmExtendedContext.SetExtendedContext(session, threadId, ref updated));
        context = updated;
    }

    public XbdmThreadStop? TryGetThreadStop(uint threadId) =>
        _sci.WithSession(session =>
        {
            var (hr, line) = session.SendCommandRaw($"ISSTOPPED THREAD={threadId}");
            if (!XbdmProtocol.IsCommandSuccess(hr))
                throw XbdmException.FromHResult("ISSTOPPED failed.", hr, line);
            var payload = XbdmProtocol.StripResponsePrefix(line);
            return XbdmNotificationParser.ParseThreadStop(payload);
        });

    public XbdmThreadInfo GetThreadInfo(uint threadId) =>
        _sci.WithSession(session =>
        {
            var (hr, _) = session.SendCommandRaw($"THREADINFO THREAD={threadId}");
            if (hr != XbdmHResults.Multiresponse)
                throw XbdmException.FromHResult("THREADINFO failed.", hr);

            uint suspend = 0, priority = 0;
            nuint tls = 0;
            while (true)
            {
                var line = session.ReceiveLine();
                if (line == ".")
                    break;
                suspend = XbdmParamParser.GetDwParam(line, "suspend", suspend);
                priority = XbdmParamParser.GetDwParam(line, "priority", priority);
                tls = XbdmParamParser.GetDwParam(line, "tlsbase", (uint)tls);
            }

            return new XbdmThreadInfo(suspend, priority, tls);
        });

    public void SuspendThread(uint threadId) => _sci.OneLineCommand($"SUSPEND THREAD={threadId}");
    public void ResumeThread(uint threadId) => _sci.OneLineCommand($"RESUME THREAD={threadId}");

    public void SetTitle(string? directory, string title, string? commandLine = null)
    {
        var builder = new StringBuilder($"TITLE NAME=\"{title}\"");
        if (!string.IsNullOrEmpty(directory))
            builder.Append($" DIR=\"{directory}\"");
        if (!string.IsNullOrEmpty(commandLine))
            builder.Append($" CMDLINE={commandLine}");
        _sci.OneLineCommand(builder.ToString());
    }

    public XbdmXbeInfo GetXbeInfo(string name) =>
        _sci.WithSession(session =>
        {
            session.SendCommand($"XBEINFO NAME=\"{name}\"");
            var lines = session.ReadMultiresponse();
            var path = string.Empty;
            uint timestamp = 0, checksum = 0, stack = 0;
            foreach (var line in lines)
            {
                path = XbdmParamParser.GetSzParam(line, "name") ?? path;
                timestamp = XbdmParamParser.GetDwParam(line, "timestamp", timestamp);
                checksum = XbdmParamParser.GetDwParam(line, "checksum", checksum);
                stack = XbdmParamParser.GetDwParam(line, "stack", stack);
            }

            return new XbdmXbeInfo(path, timestamp, checksum, stack);
        });

    public IReadOnlyList<XbdmModLoadNotification> WalkLoadedModules() =>
        _sci.WithSession(session =>
        {
            var (hr, _) = session.SendCommandRaw("MODULES");
            if (hr != XbdmHResults.Multiresponse)
                throw XbdmException.FromHResult("MODULES failed.", hr);

            var modules = new List<XbdmModLoadNotification>();
            while (true)
            {
                var line = session.ReceiveLine();
                if (line == ".")
                    break;
                if (XbdmNotificationParser.TryParseNotification($"modload {line}", out _, out var data) &&
                    data is XbdmModLoadNotification mod)
                {
                    modules.Add(mod);
                }
            }

            return modules;
        });

    public IReadOnlyList<XbdmSectionLoadNotification> WalkModuleSections(string moduleName) =>
        _sci.WithSession(session =>
        {
            var (hr, _) = session.SendCommandRaw($"SECTIONS NAME=\"{moduleName}\"");
            if (hr != XbdmHResults.Multiresponse)
                throw XbdmException.FromHResult("SECTIONS failed.", hr);

            var sections = new List<XbdmSectionLoadNotification>();
            while (true)
            {
                var line = session.ReceiveLine();
                if (line == ".")
                    break;
                var name = XbdmParamParser.GetSzParam(line, "name") ?? string.Empty;
                XbdmParamParser.TryGetDwParam(line, "base", out var baseAddr);
                XbdmParamParser.TryGetDwParam(line, "size", out var size);
                XbdmParamParser.TryGetDwParam(line, "index", out var index);
                XbdmParamParser.TryGetDwParam(line, "flags", out var flags);
                sections.Add(new XbdmSectionLoadNotification(
                    name, baseAddr, size, (ushort)index, (ushort)flags));
            }

            return sections;
        });

    public string GetModuleLongName(string shortName) =>
        _sci.WithSession(session =>
        {
            var line = session.SendCommand($"LONGNAME NAME=\"{shortName}\"");
            return XbdmParamParser.GetSzParam(line, "name") ?? XbdmProtocol.StripResponsePrefix(line);
        });

    public XbdmXtlData GetXtlData() =>
        _sci.WithSession(session =>
        {
            var (hr, _) = session.SendCommandRaw("XTLINFO");
            if (hr != XbdmHResults.Multiresponse)
                throw XbdmException.FromHResult("XTLINFO failed.", hr);

            uint lastError = 0;
            while (true)
            {
                var line = session.ReceiveLine();
                if (line == ".")
                    break;
                lastError = XbdmParamParser.GetDwParam(line, "lasterr", lastError);
            }

            return new XbdmXtlData(lastError);
        });

    public DateTime GetSystemTime() =>
        _sci.WithSession(session =>
        {
            var (hr, line) = session.SendCommandRaw("SYSTIME");
            if (hr == XbdmHResults.ClockNotSet)
                throw XbdmException.FromHResult("Console clock is not set.", hr, line);
            if (!XbdmProtocol.IsCommandSuccess(hr))
                throw XbdmException.FromHResult("SYSTIME failed.", hr, line);

            if (!XbdmParamParser.TryGetDwParam(line, "high", out var high) ||
                !XbdmParamParser.TryGetDwParam(line, "low", out var low))
            {
                throw XbdmException.FromHResult("SYSTIME response was invalid.", XbdmHResults.FileError, line);
            }

            var fileTime = ((ulong)high << 32) | low;
            return DateTime.FromFileTimeUtc((long)fileTime);
        });

    public void SetConfigValue(uint index, uint value) =>
        _sci.OneLineCommand($"SETCONFIG INDEX=0x{index:x8} VALUE=0x{value:x8}");

    public void CapControl(string action) => _sci.OneLineCommand($"CAPCONTROL {action}");

    public int SendCommand(string command, Span<char> responseBuffer)
    {
        var line = SendCommand(command);
        var copy = Math.Min(line.Length, responseBuffer.Length);
        line.AsSpan(0, copy).CopyTo(responseBuffer);
        return XbdmHResults.NoErr;
    }

    public string SendCommand(string command) =>
        _sci.WithSession(session => session.SendCommand(command));

    public void SendBinary(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        _sci.WithSession(session => session.SendBinary(bytes));
    }

    public int ReceiveBinary(Span<byte> buffer)
    {
        var temp = new byte[buffer.Length];
        _sci.WithSession(session => session.ReceiveBinary(temp));
        temp.CopyTo(buffer);
        return buffer.Length;
    }

    public uint ReceiveBinarySize() =>
        _sci.WithSession(session => session.ReceiveUInt32());

    public string ReceiveSocketLine() =>
        _sci.WithSession(session => session.ReceiveLine());

    public void DedicateConnection(string? handler)
    {
        var command = handler is null ? "DEDICATE" : $"DEDICATE HANDLER={handler}";
        _sci.OneLineCommand(command);
    }

    public XbdmCountData QueryPerformanceCounter(string name, uint type) =>
        _sci.WithSession(session =>
        {
            var (hr, _) = session.SendCommandRaw($"QUERYPC NAME=\"{name}\" TYPE=0x{type:x8}");
            if (hr != XbdmHResults.Multiresponse)
                throw XbdmException.FromHResult("QUERYPC failed.", hr);

            uint countType = 0, countLo = 0, countHi = 0, rateLo = 0, rateHi = 0;
            while (true)
            {
                var line = session.ReceiveLine();
                if (line == ".")
                    break;
                countType = XbdmParamParser.GetDwParam(line, "type", countType);
                countLo = XbdmParamParser.GetDwParam(line, "vallo", countLo);
                countHi = XbdmParamParser.GetDwParam(line, "valhi", countHi);
                rateLo = XbdmParamParser.GetDwParam(line, "ratelo", rateLo);
                rateHi = XbdmParamParser.GetDwParam(line, "ratehi", rateHi);
            }

            return new XbdmCountData(
                ((ulong)countHi << 32) | countLo,
                ((ulong)rateHi << 32) | rateLo,
                countType);
        });

    public IReadOnlyList<XbdmCountInfo> WalkPerformanceCounters() =>
        _sci.WithSession(session =>
        {
            var (hr, _) = session.SendCommandRaw("PCLIST");
            if (hr != XbdmHResults.Multiresponse)
                throw XbdmException.FromHResult("PCLIST failed.", hr);

            var counters = new List<XbdmCountInfo>();
            while (true)
            {
                var line = session.ReceiveLine();
                if (line == ".")
                    break;
                var counterName = XbdmParamParser.GetSzParam(line, "name");
                if (string.IsNullOrEmpty(counterName))
                    continue;
                var counterType = XbdmParamParser.GetDwParam(line, "type");
                counters.Add(new XbdmCountInfo(counterName, counterType));
            }

            return counters;
        });

    public void EnableGpuCounter(bool enable) =>
        _sci.OneLineCommand(enable ? "GPUCOUNT ENABLE" : "GPUCOUNT DISABLE");

    public byte[] PixelShaderSnapshot(uint x, uint y, uint flags, uint marker) =>
        ReceiveFixedBinarySnapshot($"PSSnap x={x} y={y} flags={flags} marker={marker}", 32768);

    public byte[] VertexShaderSnapshot(uint first, uint last, uint flags, uint marker) =>
        ReceiveFixedBinarySnapshot($"VSSnap first={first} last={last} flags={flags} marker={marker}", 32768);

    public byte[] MonitorFrameBuffer() =>
        _sci.WithSession(session =>
        {
            var (hr, line) = session.SendCommandRaw("screenshot");
            if (hr != XbdmHResults.Binresponse)
                throw XbdmException.FromHResult("screenshot failed.", hr, line);

            var infoLine = session.ReceiveLine();
            if (!XbdmParamParser.TryGetDwParam(infoLine, "framebuffersize", out var frameBufferSize))
                throw XbdmException.FromHResult("screenshot metadata was incomplete.", XbdmHResults.FileError, infoLine);

            var buffer = new byte[frameBufferSize];
            session.ReceiveBinary(buffer);
            return buffer;
        });

    private byte[] ReceiveFixedBinarySnapshot(string command, int size) =>
        _sci.WithSession(session =>
        {
            var (hr, line) = session.SendCommandRaw(command);
            if (hr != XbdmHResults.Binresponse)
                throw XbdmException.FromHResult($"{command} failed.", hr, line);

            var buffer = new byte[size];
            session.ReceiveBinary(buffer);
            return buffer;
        });

    private static IEnumerable<string> SplitHexPairs(string line)
    {
        for (var i = 0; i + 1 < line.Length; i += 2)
        {
            if (line[i] == '?')
            {
                yield return "?";
                yield break;
            }

            yield return line.Substring(i, 2);
        }
    }

    private static int ParseSetMemCount(string line)
    {
        var marker = "set ";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return 0;
        var start = index + marker.Length;
        var end = line.IndexOf(' ', start);
        var token = end < 0 ? line[start..] : line[start..end];
        return int.TryParse(token, out var count) ? count : 0;
    }

    private static void ApplyContextLine(ref XbdmContext context, string line, ref uint cr0Npx)
    {
        if (XbdmParamParser.TryGetDwParam(line, "esp", out var esp))
        {
            context.Esp = esp;
            context.ContextFlags |= XbdmDebugConstants.ContextControl;
        }

        if (XbdmParamParser.TryGetDwParam(line, "ebp", out var ebp))
            context.Ebp = ebp;
        if (XbdmParamParser.TryGetDwParam(line, "eip", out var eip))
            context.Eip = eip;
        if (XbdmParamParser.TryGetDwParam(line, "eflags", out var eflags))
            context.EFlags = eflags;
        if (XbdmParamParser.TryGetDwParam(line, "eax", out var eax))
        {
            context.Eax = eax;
            context.ContextFlags |= XbdmDebugConstants.ContextInteger;
        }

        if (XbdmParamParser.TryGetDwParam(line, "ebx", out var ebx))
            context.Ebx = ebx;
        if (XbdmParamParser.TryGetDwParam(line, "ecx", out var ecx))
            context.Ecx = ecx;
        if (XbdmParamParser.TryGetDwParam(line, "edx", out var edx))
            context.Edx = edx;
        if (XbdmParamParser.TryGetDwParam(line, "esi", out var esi))
            context.Esi = esi;
        if (XbdmParamParser.TryGetDwParam(line, "edi", out var edi))
            context.Edi = edi;
        if (XbdmParamParser.TryGetDwParam(line, "segcs", out var segCs))
            context.SegCs = segCs;
        if (XbdmParamParser.TryGetDwParam(line, "segss", out var segSs))
            context.SegSs = segSs;
        if (XbdmParamParser.TryGetDwParam(line, "Cr0NpxState", out var npx))
        {
            cr0Npx = npx;
            context.ContextFlags |= XbdmDebugConstants.ContextFloatingPoint;
        }
    }
}
