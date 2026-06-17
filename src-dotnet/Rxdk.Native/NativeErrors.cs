namespace Rxdk.Native;

internal static class NativeErrors
{
    internal static void ThrowLastError(string message)
    {
        var hr = XbdmNative.xbdm_last_hresult();
        var buf = new byte[512];
        XbdmNative.xbdm_last_error_message(buf, buf.Length);
        var detail = XbdmNative.ReadUtf8Buffer(buf);
        if (!string.IsNullOrWhiteSpace(detail))
            message = $"{message} {detail}";
        throw new Rxdk.Xbdm.XbdmException(message, hr);
    }
}
