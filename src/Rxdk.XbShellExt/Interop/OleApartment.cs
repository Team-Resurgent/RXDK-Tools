using System.Runtime.InteropServices;

namespace Rxdk.XbShellExt.Interop;

internal static class OleApartment
{
    private const int RpcChangedMode = unchecked((int)0x80010106);

    [ThreadStatic]
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
            return;

        var hr = OleInitialize(IntPtr.Zero);
        if (hr >= 0 || hr == RpcChangedMode)
            _initialized = true;
        else
            throw new InvalidOperationException($"OLE initialization failed (0x{hr:X8}).");
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);
}
