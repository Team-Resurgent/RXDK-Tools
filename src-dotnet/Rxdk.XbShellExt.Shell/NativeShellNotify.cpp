#include "stdafx.h"

#include "NativeShellNotify.h"

namespace
{
    bool IsContainerDropChild(IShellFolder* parent, LPCITEMIDLIST childPidl)
    {
        if (!parent || !childPidl)
            return false;

        ULONG attrs = SFGAO_FOLDER | SFGAO_FILESYSANCESTOR | SFGAO_STORAGE | SFGAO_STREAM;
        LPCITEMIDLIST child = childPidl;
        if (FAILED(parent->GetAttributesOf(1, &child, &attrs)))
            return false;

        return (attrs & (SFGAO_FOLDER | SFGAO_FILESYSANCESTOR | SFGAO_STORAGE)) != 0;
    }
}

void NotifyFolderContentsChanged(LPCITEMIDLIST folderPidl, LPCITEMIDLIST childPidl)
{
    LPCITEMIDLIST notifyPidl = folderPidl;
    CComHeapPtr<ITEMIDLIST> combined;

    CComPtr<IShellFolder> parent;
    CComPtr<IShellFolder> desktop;
    if (childPidl && folderPidl &&
        SUCCEEDED(SHGetDesktopFolder(&desktop)) &&
        desktop &&
        SUCCEEDED(desktop->BindToObject(folderPidl, nullptr, IID_PPV_ARGS(&parent))) &&
        parent &&
        IsContainerDropChild(parent, childPidl))
    {
        AttachShellPidl(combined, folderPidl, childPidl);
        if (combined)
            notifyPidl = combined;
    }

    if (notifyPidl)
        ::SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_FLUSH, notifyPidl, nullptr);
}

bool ShouldRefreshFolderAfterContextCommand(int managedCommandId)
{
    switch (managedCommandId)
    {
    case 10: // Paste
    case 11: // Delete
    case 12: // Rename
    case 13: // NewFolder
        return true;
    default:
        return false;
    }
}
