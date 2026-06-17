using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

internal readonly record struct XbdmNotificationDispatch(uint Code, object? Data);

internal static class XbdmNotificationParser
{
    private static int _execState = XbdmDebugConstants.DmnExecStart;

    internal static int ExecState => _execState;

    internal static void ResetExecState() => _execState = XbdmDebugConstants.DmnExecStart;

    internal static bool TryHandleNotification(string line, out IReadOnlyList<XbdmNotificationDispatch> dispatches)
    {
        dispatches = Array.Empty<XbdmNotificationDispatch>();
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var bang = line.IndexOf('!');
        if (bang > 0)
            return false;

        var space = line.IndexOf(' ');
        var command = space < 0 ? line : line[..space];
        var payload = space < 0 ? string.Empty : line[(space + 1)..];
        var stopThread = XbdmParamParser.TryGetParam(line, "stop", false, false)
            ? XbdmDebugConstants.StopThread
            : 0u;

        if (command.Equals("execution", StringComparison.OrdinalIgnoreCase))
            return TryHandleExecution(line, stopThread, out dispatches);

        if (command.Equals("debugstr", StringComparison.OrdinalIgnoreCase))
        {
            var debugStr = ParseDebugString(payload);
            if (string.IsNullOrEmpty(debugStr.Text))
                return false;
            dispatches = [new XbdmNotificationDispatch(XbdmDebugConstants.DmDebugStr | stopThread, debugStr)];
            return true;
        }

        if (command.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            dispatches = [new XbdmNotificationDispatch(XbdmDebugConstants.DmCreateThread | stopThread, ParseCreateThread(payload))];
            return true;
        }

        if (command.Equals("terminate", StringComparison.OrdinalIgnoreCase))
        {
            if (!XbdmParamParser.TryGetDwParam(payload, "thread", out var threadId))
                return false;
            dispatches = [new XbdmNotificationDispatch(XbdmDebugConstants.DmDestroyThread | stopThread, threadId)];
            return true;
        }

        if (command.Equals("sectload", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseSectionLoad(payload, out var section))
                return false;
            dispatches = [new XbdmNotificationDispatch(XbdmDebugConstants.DmSectionLoad | stopThread, section)];
            return true;
        }

        if (command.Equals("sectunload", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseSectionLoad(payload, out var section))
                return false;
            dispatches = [new XbdmNotificationDispatch(XbdmDebugConstants.DmSectionUnload | stopThread, section)];
            return true;
        }

        if (command.Equals("fiber", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseFiber(payload, out var fiber))
                return false;
            dispatches = [new XbdmNotificationDispatch(XbdmDebugConstants.DmFiber | stopThread, fiber)];
            return true;
        }

        if (command.Equals("assert", StringComparison.OrdinalIgnoreCase))
        {
            if (!XbdmAssertBuffer.TryAppend(payload, out var completed))
                return false;
            dispatches = [new XbdmNotificationDispatch(XbdmDebugConstants.DmAssert | stopThread, completed!)];
            return true;
        }

        if (!TryParseCoreNotification(command, payload, line, out var code, out var data))
            return false;

        if (code == XbdmDebugConstants.DmRip)
        {
            XbdmParamParser.TryGetDwParam(payload, "thread", out var threadId);
            data = new XbdmDebugStringNotification(threadId, XbdmParamParser.GetSzParam(payload, "string") ?? string.Empty);
        }

        dispatches = [new XbdmNotificationDispatch((uint)code | stopThread, data)];
        return true;
    }

    internal static bool TryGetExternalPrefix(string line, out string prefix)
    {
        prefix = string.Empty;
        var bang = line.IndexOf('!');
        if (bang <= 0)
            return false;
        prefix = line[..bang];
        return prefix.Length > 0;
    }

    internal static XbdmThreadStop? ParseThreadStop(string line)
    {
        var payload = line.StartsWith("200- ", StringComparison.Ordinal) ? line[5..] : line;
        if (!TryHandleNotification(payload, out var dispatches) || dispatches.Count == 0)
            return null;
        var first = dispatches[0];
        return new XbdmThreadStop(first.Code, first.Data);
    }

    internal static bool TryParseNotification(string line, out uint notification, out object? data)
    {
        notification = XbdmDebugConstants.DmNone;
        data = null;
        if (!TryHandleNotification(line, out var dispatches) || dispatches.Count == 0)
            return false;
        notification = dispatches[0].Code;
        data = dispatches[0].Data;
        return true;
    }

    private static bool TryHandleExecution(string line, uint stopThread, out IReadOnlyList<XbdmNotificationDispatch> dispatches)
    {
        dispatches = Array.Empty<XbdmNotificationDispatch>();
        int newState;
        if (XbdmParamParser.TryGetParam(line, "started", false, false))
            newState = XbdmDebugConstants.DmnExecStart;
        else if (XbdmParamParser.TryGetParam(line, "stopped", false, false))
            newState = XbdmDebugConstants.DmnExecStop;
        else if (XbdmParamParser.TryGetParam(line, "pending", false, false))
            newState = XbdmDebugConstants.DmnExecPending;
        else if (XbdmParamParser.TryGetParam(line, "rebooting", false, false))
            newState = XbdmDebugConstants.DmnExecReboot;
        else
            return false;

        if (newState == _execState)
            return false;

        _execState = newState;
        dispatches = [new XbdmNotificationDispatch(XbdmDebugConstants.DmExec | stopThread, newState)];
        return true;
    }

    private static bool TryParseCoreNotification(
        string command,
        string payload,
        string fullLine,
        out int code,
        out object? data)
    {
        code = XbdmDebugConstants.DmNone;
        data = null;

        if (command.Equals("break", StringComparison.OrdinalIgnoreCase))
        {
            code = XbdmDebugConstants.DmBreak;
            data = ParseBreak(payload);
            return true;
        }

        if (command.Equals("singlestep", StringComparison.OrdinalIgnoreCase))
        {
            code = XbdmDebugConstants.DmSingleStep;
            data = ParseBreak(payload);
            return true;
        }

        if (command.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            code = XbdmDebugConstants.DmDataBreak;
            data = ParseDataBreak(payload);
            return true;
        }

        if (command.Equals("exception", StringComparison.OrdinalIgnoreCase))
        {
            code = XbdmDebugConstants.DmException;
            data = ParseException(payload);
            return true;
        }

        if (command.Equals("modload", StringComparison.OrdinalIgnoreCase))
        {
            code = XbdmDebugConstants.DmModLoad;
            data = ParseModLoad(payload);
            return true;
        }

        if (command.Equals("createthread", StringComparison.OrdinalIgnoreCase))
        {
            code = XbdmDebugConstants.DmCreateThread;
            data = ParseCreateThread(payload);
            return true;
        }

        if (command.Equals("rip", StringComparison.OrdinalIgnoreCase))
        {
            code = XbdmDebugConstants.DmRip;
            return true;
        }

        if (command.Equals("exec", StringComparison.OrdinalIgnoreCase))
        {
            code = XbdmDebugConstants.DmExec;
            if (XbdmParamParser.TryGetDwParam(payload, "state", out var state))
                data = state;
            return true;
        }

        return false;
    }

    private static XbdmBreakNotification ParseBreak(string line)
    {
        XbdmParamParser.TryGetDwParam(line, "addr", out var addr);
        XbdmParamParser.TryGetDwParam(line, "thread", out var thread);
        return new XbdmBreakNotification(addr, thread);
    }

    private static XbdmDataBreakNotification ParseDataBreak(string line)
    {
        var brk = ParseBreak(line);
        uint breakType = XbdmDebugConstants.DmbreakNone;
        nuint dataAddress = 0;
        if (XbdmParamParser.TryGetDwParam(line, "write", out var writeAddr))
        {
            breakType = XbdmDebugConstants.DmbreakWrite;
            dataAddress = writeAddr;
        }
        else if (XbdmParamParser.TryGetDwParam(line, "read", out var readAddr))
        {
            breakType = XbdmDebugConstants.DmbreakReadWrite;
            dataAddress = readAddr;
        }
        else if (XbdmParamParser.TryGetDwParam(line, "execute", out var execAddr))
        {
            breakType = XbdmDebugConstants.DmbreakExecute;
            dataAddress = execAddr;
        }

        return new XbdmDataBreakNotification(brk.Address, brk.ThreadId, breakType, dataAddress);
    }

    private static XbdmDebugStringNotification ParseDebugString(string line)
    {
        XbdmParamParser.TryGetDwParam(line, "thread", out var thread);
        var text = XbdmParamParser.GetSzParam(line, "string") ?? string.Empty;
        return new XbdmDebugStringNotification(thread, text);
    }

    private static XbdmModLoadNotification ParseModLoad(string line)
    {
        var name = XbdmParamParser.GetSzParam(line, "name") ?? string.Empty;
        XbdmParamParser.TryGetDwParam(line, "base", out var baseAddr);
        XbdmParamParser.TryGetDwParam(line, "size", out var size);
        XbdmParamParser.TryGetDwParam(line, "timestamp", out var timestamp);
        XbdmParamParser.TryGetDwParam(line, "checksum", out var checksum);
        uint flags = 0;
        if (XbdmParamParser.TryGetParam(line, "tls", false, true))
            flags |= XbdmDebugConstants.DmnModflagTls;
        if (XbdmParamParser.TryGetParam(line, "xbe", false, true))
            flags |= XbdmDebugConstants.DmnModflagXbe;
        return new XbdmModLoadNotification(name, baseAddr, size, timestamp, checksum, flags);
    }

    private static XbdmCreateThreadNotification ParseCreateThread(string line)
    {
        XbdmParamParser.TryGetDwParam(line, "thread", out var thread);
        XbdmParamParser.TryGetDwParam(line, "start", out var start);
        return new XbdmCreateThreadNotification(thread, start);
    }

    private static XbdmExceptionNotification ParseException(string line)
    {
        XbdmParamParser.TryGetDwParam(line, "code", out var code);
        XbdmParamParser.TryGetDwParam(line, "thread", out var thread);
        XbdmParamParser.TryGetDwParam(line, "address", out var address);
        uint flags = 0;
        if (XbdmParamParser.TryGetParam(line, "first", false, false))
            flags |= XbdmDebugConstants.DmExceptFirstChance;
        if (XbdmParamParser.TryGetParam(line, "noncont", false, false))
            flags |= XbdmDebugConstants.DmExceptNoncontinuable;
        XbdmParamParser.TryGetDwParam(line, "read", out var info1);
        var hasWrite = XbdmParamParser.TryGetDwParam(line, "write", out var info1Write);
        if (hasWrite)
            info1 = info1Write;
        var info0 = hasWrite ? 1u : 0u;
        return new XbdmExceptionNotification(thread, code, address, flags, info0, info1);
    }

    private static bool TryParseSectionLoad(string line, out XbdmSectionLoadNotification section)
    {
        section = new XbdmSectionLoadNotification(string.Empty, 0, 0, 0, 0);
        var name = XbdmParamParser.GetSzParam(line, "name");
        if (string.IsNullOrEmpty(name))
            return false;
        if (!XbdmParamParser.TryGetDwParam(line, "base", out var baseAddr))
            return false;
        if (!XbdmParamParser.TryGetDwParam(line, "size", out var size))
            return false;
        XbdmParamParser.TryGetDwParam(line, "index", out var index);
        XbdmParamParser.TryGetDwParam(line, "flags", out var flags);
        section = new XbdmSectionLoadNotification(name, baseAddr, size, (ushort)index, (ushort)flags);
        return true;
    }

    private static bool TryParseFiber(string line, out XbdmFiberNotification fiber)
    {
        fiber = new XbdmFiberNotification(0, false, 0);
        if (!XbdmParamParser.TryGetDwParam(line, "id", out var fiberId))
            return false;

        if (XbdmParamParser.TryGetDwParam(line, "start", out var start))
        {
            fiber = new XbdmFiberNotification(fiberId, true, start);
            return true;
        }

        if (XbdmParamParser.TryGetParam(line, "delete", false, true))
        {
            fiber = new XbdmFiberNotification(fiberId, false, 0);
            return true;
        }

        return false;
    }
}
