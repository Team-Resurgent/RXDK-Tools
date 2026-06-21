#pragma once

HRESULT CreateNativeDataObject(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    REFIID riid,
    void** ppv);
