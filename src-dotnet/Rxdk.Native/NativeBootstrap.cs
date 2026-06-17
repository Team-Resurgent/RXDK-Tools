using System.Runtime.CompilerServices;
using Rxdk.Xbdm;

namespace Rxdk.Native;

internal static class NativeBootstrap
{
    [ModuleInitializer]
    internal static void Register()
    {
        var backend = Environment.GetEnvironmentVariable("RXDK_XBDM_BACKEND");
        if (!string.Equals(backend, "native", StringComparison.OrdinalIgnoreCase) || !OperatingSystem.IsWindows())
            return;

        XbdmClients.Register(() => new NativeXbdmClient());
    }
}
