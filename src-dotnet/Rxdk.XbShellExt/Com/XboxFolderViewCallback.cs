using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Shell;

namespace Rxdk.XbShellExt.Com;

internal sealed class XboxFolderViewCallback : IShellFolderViewCB
{
    private const uint SfvmThisIdList = 41;
    private const uint SfvmGetNotify = 49;
    private const uint SfvmBackgroundEnumDone = 48;
    private const uint SfvmBackgroundEnum = 32;
    private const uint SfvmDontCustomize = 56;
    private const uint SfvmGetZone = 58;
    private const uint SfvmFsNotify = 14;
    private const uint SfvmDefItemCount = 26;
    private const uint SfvmDidDragDrop = 36;

    private const int ShcneDiskEvents = unchecked((int)0x0002381F);
    private const int ShcneAssocChanged = 0x08000000;
    private const int ShcneRmdir = 0x00000010;
    private const int ShcneDelete = 0x00000002;
    private const int ShcneMkdir = 0x00000008;
    private const int ShcneCreate = 0x00000002;
    private const int ShcneRenameFolder = 0x00020000;
    private const int ShcneRenameItem = 0x00000001;
    private const int ShcneUpdateItem = 0x00002000;
    private const int ShcneAttributes = 0x00000800;

    private static readonly int NotifyEvents =
        ShcneDiskEvents |
        ShcneAssocChanged |
        ShcneRmdir |
        ShcneDelete |
        ShcneMkdir |
        ShcneCreate |
        ShcneRenameFolder |
        ShcneRenameItem |
        ShcneUpdateItem |
        ShcneAttributes;

    private readonly XboxFolder _folder;

    public XboxFolderViewCallback(XboxFolder folder) => _folder = folder;

    public int MessageSFVCB(uint uMsg, nint wParam, nint lParam)
    {
        switch (uMsg)
        {
        case SfvmThisIdList:
            if (lParam == 0)
                return HResults.InvalidArg;
            var hr = _folder.GetThisIdList(out var thisPidl);
            if (hr < 0)
                return hr;
            Marshal.WriteIntPtr(lParam, thisPidl);
            return HResults.Ok;

        case SfvmGetNotify:
            if (wParam == 0 || lParam == 0)
                return HResults.InvalidArg;
            hr = _folder.GetCurFolderAbsolute(out var notifyPidl);
            if (hr < 0)
                return hr;
            Marshal.WriteIntPtr(wParam, notifyPidl);
            Marshal.WriteInt32(lParam, NotifyEvents);
            return HResults.Ok;

        case SfvmDefItemCount:
            if (lParam == 0)
                return HResults.InvalidArg;
            try
            {
                var count = XboxShellItemFactory.ListChildren(_folder.FullPath).Count;
                Marshal.WriteInt32(lParam, count > 0 ? count : 1);
            }
            catch
            {
                Marshal.WriteInt32(lParam, 1);
            }
            return HResults.Ok;

        case SfvmBackgroundEnum:
            return HResults.Ok;

        case SfvmBackgroundEnumDone:
        case SfvmFsNotify:
            return HResults.Ok;

        case SfvmDontCustomize:
            if (lParam != 0)
                Marshal.WriteInt32(lParam, 0);
            return HResults.Ok;

        case SfvmGetZone:
            if (lParam == 0)
                return HResults.InvalidArg;
            Marshal.WriteInt32(lParam, 0); // URLZONE_LOCAL_MACHINE
            return HResults.Ok;

        case SfvmDidDragDrop:
            return OnDidDragDrop((uint)wParam, lParam);

        default:
            return HResults.NotImpl;
        }
    }

    private static int OnDidDragDrop(uint dropEffect, nint dataObjectPtr)
    {
        if (dropEffect != OleConstants.DropeffectMove || dataObjectPtr == 0)
            return HResults.Ok;

        var iidAsync = new Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4");
        try
        {
            if (Marshal.QueryInterface(dataObjectPtr, ref iidAsync, out var asyncPtr) < 0 || asyncPtr == 0)
                return HResults.False;

            try
            {
                var async = (IAsyncOperation)Marshal.GetObjectForIUnknown(asyncPtr)!;
                if (async.InOperation(out var inAsyncOp) >= 0 && inAsyncOp)
                    return HResults.Ok;
            }
            finally
            {
                Marshal.Release(asyncPtr);
            }
        }
        catch
        {
        }

        return HResults.False;
    }
}
