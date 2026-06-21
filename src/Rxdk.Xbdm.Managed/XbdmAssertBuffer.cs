using System.Text;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

internal static class XbdmAssertBuffer
{
    private static readonly object Gate = new();
    private static uint _threadId;
    private static readonly StringBuilder Text = new();

    internal static bool TryAppend(string line, out XbdmDebugStringNotification? completed)
    {
        completed = null;
        if (!XbdmParamParser.TryGetDwParam(line, "thread", out var threadId))
            return false;

        lock (Gate)
        {
            if (_threadId != threadId)
            {
                Text.Clear();
                _threadId = threadId;
            }

            var chunk = XbdmParamParser.GetSzParam(line, "string");
            if (!string.IsNullOrEmpty(chunk))
            {
                AppendSuffix(line, chunk);
                Text.Append(chunk);
            }

            if (!XbdmParamParser.TryGetParam(line, "prompt", false, false))
                return false;

            completed = new XbdmDebugStringNotification(_threadId, Text.ToString());
            Text.Clear();
            _threadId = 0;
            return true;
        }
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            Text.Clear();
            _threadId = 0;
        }
    }

    private static void AppendSuffix(string line, string chunk)
    {
        if (XbdmParamParser.TryGetParam(line, "cr", false, false))
            Text.Append('\r');
        else if (XbdmParamParser.TryGetParam(line, "lf", false, false))
            Text.Append('\n');
        else if (XbdmParamParser.TryGetParam(line, "crlf", false, false))
            Text.Append("\r\n");
        else if (chunk.Length > 0 && Text.Length > 0)
        {
            var last = chunk[^1];
            if (last is not '\r' and not '\n')
                Text.Append(' ');
        }
    }
}
