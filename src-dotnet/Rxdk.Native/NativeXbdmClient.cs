using Rxdk.Xbdm;
using XbdmConst = Rxdk.Xbdm.XbdmConstants;

namespace Rxdk.Native;

public sealed class NativeXbdmClient : IXbdmClient
{
    private int _initCount;
    private readonly object _initLock = new();

    public string BackendName => "native";

    public void Initialize()
    {
        lock (_initLock)
        {
            if (_initCount == 0)
            {
                var info = new NativeAbiInfo();
                if (XbdmNative.xbdm_init(ref info) != 0)
                    NativeErrors.ThrowLastError("xbdm_init failed");
            }

            _initCount++;
        }
    }

    public void Shutdown()
    {
        lock (_initLock)
        {
            if (_initCount <= 0)
                return;

            _initCount--;
            if (_initCount == 0)
                XbdmNative.xbdm_shutdown();
        }
    }

    public void Dispose() => Shutdown();

    public string GetDefaultConsoleName()
    {
        var buf = new byte[XbdmConst.MaxName];
        if (XbdmNative.xbdm_get_default_console_name(buf, buf.Length) != 0)
            NativeErrors.ThrowLastError("Could not read default console name.");
        return XbdmNative.ReadUtf8Buffer(buf);
    }

    public void SetDefaultConsoleName(string name)
    {
        if (XbdmNative.xbdm_set_default_console_name(name) != 0)
            NativeErrors.ThrowLastError("Could not set default console name.");
    }

    public IXbdmConnection Connect(string consoleName)
    {
        if (XbdmNative.xbdm_connect(consoleName, out var handle) != 0 || handle == IntPtr.Zero)
            NativeErrors.ThrowLastError($"Could not connect to '{consoleName}'.");

        return new NativeXbdmConnection(consoleName, handle);
    }
}
