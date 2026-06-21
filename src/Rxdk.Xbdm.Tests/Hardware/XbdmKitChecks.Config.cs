namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private static bool EnvFlag(string suffix) => KitTestConfig.Flag(suffix);

    private static bool HasPassword() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD"));

    /// <summary>
    /// Password used when the security round-trip locks the kit. Defaults to <c>test</c> so an
    /// unlocked kit can be exercised without extra env vars; override with RXDK_KIT_SECURITY_PASSWORD.
    /// </summary>
    internal static string SecurityTestPassword() =>
        KitTestConfig.Env("SECURITY_PASSWORD")
        ?? Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")
        ?? Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD")
        ?? "test";

    private static bool AllowLaunchTests() => EnvFlag("ALLOW_LAUNCH") || AllowExecTests();

    private static bool AllowExecTests() => EnvFlag("ALLOW_EXEC");

    private static bool AllowRebootTests() => EnvFlag("ALLOW_REBOOT");

    private static bool AllowSecurityTests() => EnvFlag("ALLOW_SECURITY");

    private static bool AllowBridgeTests() =>
        EnvFlag("ALLOW_BRIDGE") || AllowExecTests();

    private static bool RestoreAfterExec() =>
        !string.Equals(KitTestConfig.Env("NO_RESTORE"), "1", StringComparison.Ordinal);
}
