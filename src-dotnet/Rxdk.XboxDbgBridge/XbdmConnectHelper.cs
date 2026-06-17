using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XboxDbgBridge;

internal static class XbdmConnectHelper
{
    internal static IXbdmConnection Connect(IXbdmClient client, string console)
    {
        var password = Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD")
            ?? Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD");
        if (client is ManagedXbdmClient managed)
        {
            return string.IsNullOrWhiteSpace(password)
                ? managed.Connect(console)
                : managed.Connect(console, new XbdmConnectOptions { AdminPassword = password });
        }

        var connection = client.Connect(console);
        if (!string.IsNullOrWhiteSpace(password))
            connection.UseSecureConnection(password);
        return connection;
    }
}
