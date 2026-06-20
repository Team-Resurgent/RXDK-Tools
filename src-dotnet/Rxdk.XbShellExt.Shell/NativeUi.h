#pragma once

#include <shlobj.h>

struct __declspec(uuid("A7F2C9E1-4B8D-4A2E-9C31-8E5D6F0A2B44")) IXboxShellExtUi : public IUnknown
{
    STDMETHOD(ShowPropertiesForSelection)(HWND hwnd, LPCITEMIDLIST childPidl) PURE;
    STDMETHOD(ShowSecurityForSelection)(HWND hwnd) PURE;
    STDMETHOD(ShowAddConsoleWizard)(HWND hwnd) PURE;
    STDMETHOD(InvokeContextCommand)(HWND hwnd, LPCITEMIDLIST childPidl, int command) PURE;
    STDMETHOD(GetDragFileGroupDescriptor)(UINT cidl, LPCITEMIDLIST* apidl, HGLOBAL* phGlobal) PURE;
    STDMETHOD(GetDragFileContentsStream)(LONG index, IUnknown** ppStream) PURE;
    STDMETHOD(PerformDrop)(HWND hwnd, LPCITEMIDLIST childPidl, IDataObject* pDataObject, DWORD* pdwEffect) PURE;
};
