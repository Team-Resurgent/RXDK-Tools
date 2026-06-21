namespace Rxdk.XbWatson.Core;

public enum WatsonAssertChoice
{
    Reboot,
    Break,
    Continue,
}

public enum WatsonRipChoice
{
    Reboot,
    Dump,
    Break,
    Continue,
}

public enum WatsonExceptionChoice
{
    Reboot,
    Dump,
    Continue,
}

public sealed class WatsonAssertEvent
{
    public required string AssertText { get; init; }
    public required uint ThreadId { get; init; }
    public required string ConsoleName { get; init; }
    public string LaunchPath { get; init; } = "";
}

public sealed class WatsonRipEvent
{
    public required string RipText { get; init; }
    public required uint ThreadId { get; init; }
    public required string ConsoleName { get; init; }
}

public sealed class WatsonExceptionEvent
{
    public required uint ThreadId { get; init; }
    public required uint Code { get; init; }
    public required nuint Address { get; init; }
    public required bool WriteViolation { get; init; }
    public required nuint FaultAddress { get; init; }
    public required string ConsoleName { get; init; }
}

public interface IWatsonEventSink
{
    void OnLog(string text);
    Task<WatsonAssertChoice> OnAssertAsync(WatsonAssertEvent evt);
    Task<WatsonRipChoice> OnRipAsync(WatsonRipEvent evt);
    Task<WatsonExceptionChoice> OnExceptionAsync(WatsonExceptionEvent evt);
    void OnWarning(string text);
    void OnXboxConnected();
}
