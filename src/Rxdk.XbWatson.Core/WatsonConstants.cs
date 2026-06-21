namespace Rxdk.XbWatson.Core;

/// <summary>Dialog IDs from original xbWatson resource.h — stored in crash dump event type field.</summary>
public static class WatsonDialogIds
{
    public const uint Exception = 134;
    public const uint Rip = 135;
}

public static class WatsonExceptionCodes
{
    public const uint Breakpoint = 0x80000003;
    public const uint AccessViolation = 0xC0000005;
}

public static class WatsonLogLimits
{
    public const int MaxLines = 100000;
    public const int LinesToCut = 2000;
}
