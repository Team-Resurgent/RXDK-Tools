using System.Runtime.InteropServices;

namespace Rxdk.XbShellExt.Interop;

/// <summary>
/// Publishes a COM error description for the current apartment so shell copy UI
/// can show a useful message instead of "Unspecified error".
/// </summary>
internal static class ComErrorInfo
{
    private const string Source = "Xbox Neighborhood";

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int CreateErrorInfo(out ICreateErrorInfo pCreateErrorInfo);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int SetErrorInfo(uint dwReserved, IErrorInfo? pErrorInfo);

    public static void SetMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            if (CreateErrorInfo(out var createInfo) < 0 || createInfo == null)
                return;

            createInfo.SetSource(Source);
            createInfo.SetDescription(message);

            var unk = Marshal.GetIUnknownForObject(createInfo);
            try
            {
                var iid = typeof(IErrorInfo).GUID;
                Marshal.QueryInterface(unk, in iid, out var errorInfoPtr);
                if (errorInfoPtr == IntPtr.Zero)
                    return;

                try
                {
                    var errorInfo = (IErrorInfo)Marshal.GetObjectForIUnknown(errorInfoPtr)!;
                    SetErrorInfo(0, errorInfo);
                }
                finally
                {
                    Marshal.Release(errorInfoPtr);
                }
            }
            finally
            {
                Marshal.Release(unk);
            }
        }
        catch
        {
        }
    }

    [ComImport]
    [Guid("22C0331A-470B-11D0-A42F-00A0C90FF208")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateErrorInfo
    {
        void SetGUID(ref Guid guid);
        void SetSource([MarshalAs(UnmanagedType.LPWStr)] string source);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void SetHelpFile([MarshalAs(UnmanagedType.LPWStr)] string helpFile);
        void SetHelpContext(uint helpContext);
    }

    [ComImport]
    [Guid("1C026634-7553-11D0-9267-00AA00C27C5C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IErrorInfo
    {
        void GetGUID(out Guid guid);
        [PreserveSig]
        int GetSource([MarshalAs(UnmanagedType.BStr)] out string source);
        [PreserveSig]
        int GetDescription([MarshalAs(UnmanagedType.BStr)] out string description);
        [PreserveSig]
        int GetHelpFile([MarshalAs(UnmanagedType.BStr)] out string helpFile);
        [PreserveSig]
        int GetHelpContext(out uint helpContext);
    }
}
