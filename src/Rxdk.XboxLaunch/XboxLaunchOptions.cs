namespace Rxdk.XboxLaunch;

public sealed class XboxLaunchOptions
{
    public string? Directory { get; set; }
    public string? Title { get; set; }
    public string CommandLine { get; set; } = string.Empty;
    public string? ConsoleName { get; set; }
    public bool Reboot { get; set; } = true;
    public int TimeoutMs { get; set; } = 120_000;
}

public enum XboxLaunchExitCode
{
    Success = 0,
    Error = 1,
    NoConsole = 2,
}
