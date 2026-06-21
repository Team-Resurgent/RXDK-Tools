using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XboxDbgBridge;

internal static class XbdmConnectHelper
{
    internal static IXbdmConnection Connect(ManagedXbdmClient client, string console)
    {
        var password = Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD")
            ?? Environment.GetEnvironmentVariable("RXDK_TEST_PASSWORD");
        return string.IsNullOrWhiteSpace(password)
            ? client.Connect(console)
            : client.Connect(console, new XbdmConnectOptions { AdminPassword = password });
    }
}
