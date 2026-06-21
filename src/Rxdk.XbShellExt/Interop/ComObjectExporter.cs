using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Rxdk.XbShellExt.Interop;

internal static class ComObjectExporter
{
    private static readonly ConcurrentDictionary<nint, object> Alive = new();

    public static nint ExportToIUnknown(object comObject)
    {
        var unk = Marshal.GetIUnknownForObject(comObject);
        if (unk == 0)
            return 0;

        Alive[unk] = comObject;

        if (CoCreateFreeThreadedMarshaler(unk, out var marshaler) >= 0 && marshaler != 0)
        {
            Alive[marshaler] = comObject;
            Marshal.Release(unk);
            return marshaler;
        }

        return unk;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateFreeThreadedMarshaler(nint punkOuter, out nint ppunkMarshal);
}
