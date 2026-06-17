using System.Runtime.CompilerServices;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

internal static class ManagedBootstrap
{
    [ModuleInitializer]
    internal static void Register()
    {
        var backend = Environment.GetEnvironmentVariable("RXDK_XBDM_BACKEND");
        if (!string.Equals(backend, "native", StringComparison.OrdinalIgnoreCase))
            XbdmClients.Register(() => new ManagedXbdmClient());
    }
}
