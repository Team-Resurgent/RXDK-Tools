#pragma once

HRESULT CreateNativeDropTarget(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    HWND hwndOwner,
    REFIID riid,
    void** ppv);
