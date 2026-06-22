using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Interop;

namespace Rxdk.XbShellExt.Com;

[ComVisible(true)]
[Guid(ComGuids.XboxShellExtUi)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXboxShellExtUi
{
    [PreserveSig]
    int ShowPropertiesForSelection(nint hwnd, nint childPidl);

    [PreserveSig]
    int ShowSecurityForSelection(nint hwnd);

    [PreserveSig]
    int ShowAddConsoleWizard(nint hwnd);

    [PreserveSig]
    int InvokeContextCommand(nint hwnd, nint childPidl, int command);

    [PreserveSig]
    int GetDragFileGroupDescriptor(uint cidl, nint apidl, out nint phGlobal);

    [PreserveSig]
    int GetDragFileContentsStream(int index, out nint ppStream);

    [PreserveSig]
    int PerformDrop(nint hwnd, nint childPidl, nint dataObjectUnk, ref uint pdwEffect);

    [PreserveSig]
    int InvokeContextCommandForSelection(nint hwnd, uint cidl, nint apidl, int command);
}
