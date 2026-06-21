using Rxdk.Xbdm;

namespace Rxdk.XbWatson.Core;

public static class WatsonKernelDebug
{
    public const string TimestampsEnvironmentVariable = "XBWATSON_TIMESTAMPS";

    /// <summary>
    /// Kernel debug strings arrive as DM_DEBUGSTR with thread id zero.
    /// </summary>
    public static bool IsKernelDebugString(uint threadId) => threadId == 0;

    public static bool TrySetDpctrace(IXbdmDebugConnection debug, bool enabled)
    {
        try
        {
            debug.SendCommand($"dbgoptions dpctrace={(enabled ? 1 : 0)}");
            return true;
        }
        catch (XbdmException)
        {
            return false;
        }
    }

    public static bool ResponseMentionsDpctrace(string? response) =>
        !string.IsNullOrEmpty(response) &&
        response.Contains("dpctrace", StringComparison.OrdinalIgnoreCase);

    public static bool TryProbeDpctraceSupport(IXbdmDebugConnection debug, bool currentEnabled, out bool available)
    {
        available = false;
        try
        {
            var response = debug.SendCommand($"dbgoptions dpctrace={(currentEnabled ? 1 : 0)}");
            available = ResponseMentionsDpctrace(response) || TrySetDpctrace(debug, currentEnabled);
            return true;
        }
        catch (XbdmException)
        {
            return false;
        }
    }
}
