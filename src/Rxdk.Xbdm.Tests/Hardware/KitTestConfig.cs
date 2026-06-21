namespace Rxdk.Xbdm.Tests.Hardware;

internal static class KitTestConfig
{
    internal static string? Env(string suffix) =>
        Environment.GetEnvironmentVariable($"RXDK_KIT_{suffix}");

    internal static bool Flag(string suffix) =>
        string.Equals(Env(suffix), "1", StringComparison.Ordinal);

    internal static bool PauseEnabled() =>
        Env("PAUSE") is "1" or "true";

    internal static int TimeoutSeconds(string suffix, int defaultSeconds)
    {
        var raw = Env(suffix);
        return int.TryParse(raw, out var seconds) && seconds > 0 ? seconds : defaultSeconds;
    }
}
