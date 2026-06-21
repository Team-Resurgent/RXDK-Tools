#pragma once

HRESULT CreateNativeExtractIcon(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    REFIID riid,
    void** ppv);

extern "C" HRESULT __stdcall XbShellExt_CreateNativeExtractIcon(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    REFIID riid,
    void** ppv);
