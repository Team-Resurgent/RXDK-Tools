namespace Rxdk.Xbdm.Tests.Parity;

internal static partial class XbdmParityChecks
{
    private static bool EnvFlag(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    private static bool HasPassword() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD"));

    /// <summary>
    /// Password used when the security round-trip locks the kit. Defaults to <c>test</c> so an
    /// unlocked kit can be exercised without extra env vars; override with RXDK_PARITY_SECURITY_PASSWORD.
    /// </summary>
    internal static string SecurityTestPassword() =>
        Environment.GetEnvironmentVariable("RXDK_PARITY_SECURITY_PASSWORD")
        ?? Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD")
        ?? Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD")
        ?? "test";

    private static bool AllowLaunchTests() => EnvFlag("RXDK_PARITY_ALLOW_LAUNCH") || AllowExecTests();

    private static bool AllowExecTests() => EnvFlag("RXDK_PARITY_ALLOW_EXEC");

    private static bool AllowRebootTests() => EnvFlag("RXDK_PARITY_ALLOW_REBOOT");

    private static bool AllowSecurityTests() => EnvFlag("RXDK_PARITY_ALLOW_SECURITY");

    private static bool AllowBridgeTests() =>
        EnvFlag("RXDK_PARITY_ALLOW_BRIDGE") || AllowExecTests();

    private static bool RestoreAfterExec() =>
        !string.Equals(Environment.GetEnvironmentVariable("RXDK_PARITY_NO_RESTORE"), "1", StringComparison.Ordinal);
}
