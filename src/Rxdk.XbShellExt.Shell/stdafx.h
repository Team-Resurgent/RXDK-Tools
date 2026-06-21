#pragma once

#define STRICT
#define _WIN32_WINNT 0x0A00
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <propkey.h>
#include <ShObjIdl_core.h>
#include <shlguid.h>
#include <shlwapi.h>
#include <strsafe.h>

#define _ATL_APARTMENT_THREADED
#include <atlbase.h>
extern CComModule _Module;
#include <atlcom.h>

#include <memory>
#include <string>
#include <vector>

#include "ShellGuids.h"

HRESULT CreateManagedFolder(REFIID riid, void** ppv);
HRESULT WrapWithFreeThreadedMarshaler(IUnknown* inner, REFIID riid, void** ppv);

inline ITEMIDLIST* CloneShellPidl(PCUIDLIST_RELATIVE pidl)
{
    if (!pidl)
        return nullptr;

    const SIZE_T cb = ILGetSize(reinterpret_cast<LPCITEMIDLIST>(pidl));
    if (cb == 0)
        return nullptr;

    auto* copy = static_cast<ITEMIDLIST*>(CoTaskMemAlloc(cb));
    if (!copy)
        return nullptr;

    memcpy(copy, reinterpret_cast<const void*>(pidl), cb);
    return copy;
}

inline ITEMIDLIST* CombineShellPidl(PCUIDLIST_RELATIVE pidl1, PCUIDLIST_RELATIVE pidl2)
{
    if (!pidl1 || !pidl2)
        return nullptr;

    const PIDLIST_RELATIVE combined = ILCombine(pidl1, pidl2);
    if (!combined)
        return nullptr;

    const SIZE_T cb = ILGetSize(reinterpret_cast<LPCITEMIDLIST>(combined));
    auto* copy = static_cast<ITEMIDLIST*>(CoTaskMemAlloc(cb));
    if (!copy)
    {
        CoTaskMemFree(combined);
        return nullptr;
    }

    memcpy(copy, reinterpret_cast<const void*>(combined), cb);
    CoTaskMemFree(combined);
    return copy;
}

inline void AttachShellPidl(CComHeapPtr<ITEMIDLIST>& dest, LPCITEMIDLIST pidl)
{
    dest.Free();
    dest.Attach(CloneShellPidl(reinterpret_cast<PCUIDLIST_RELATIVE>(pidl)));
}

inline void AttachShellPidl(CComHeapPtr<ITEMIDLIST>& dest, LPCITEMIDLIST pidl1, LPCITEMIDLIST pidl2)
{
    dest.Free();
    if (!pidl1)
        dest.Attach(CloneShellPidl(reinterpret_cast<PCUIDLIST_RELATIVE>(pidl2)));
    else
        dest.Attach(CombineShellPidl(
            reinterpret_cast<PCUIDLIST_RELATIVE>(pidl1),
            reinterpret_cast<PCUIDLIST_RELATIVE>(pidl2)));
}

#if !defined(_M_IX86)
HRESULT XdkCreateShellFolderView(const SFV_CREATE* pcsfv, IShellView** ppsv);
#endif

#define RH(expr)            \
    do {                    \
        const HRESULT _hr = (expr); \
        if (FAILED(_hr))    \
            return _hr;     \
    } while (0)
