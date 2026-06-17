using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XboxDbgBridge;

internal static class BridgeBootstrap
{
    private static int _registered;

    internal static void RegisterBackend()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
            return;

        XbdmClients.Register(() => new ManagedXbdmClient());
    }
}
