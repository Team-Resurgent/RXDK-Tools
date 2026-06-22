#include "stdafx.h"
#include "ShellTrace.h"

namespace
{
    CComAutoCriticalSection g_createFolderLock;
}

HRESULT CreateManagedFolder(REFIID riid, void** ppv)
{
    XB_TRACE_SCOPE("CreateManagedFolder");
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;
    CComCritSecLock<CComAutoCriticalSection> lock(g_createFolderLock);
    const HRESULT hr = CoCreateInstance(CLSID_XboxFolderManaged, nullptr, CLSCTX_INPROC_SERVER, riid, ppv);
    ShellTraceHr("CreateManagedFolder CoCreate CC45", hr);
    return hr;
}

HRESULT WrapWithFreeThreadedMarshaler(IUnknown* inner, REFIID riid, void** ppv)
{
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;
    if (!inner)
        return E_POINTER;

    CComPtr<IUnknown> marshaler;
    const HRESULT hrMarsh = CoCreateFreeThreadedMarshaler(inner, &marshaler);
    if (SUCCEEDED(hrMarsh) && marshaler)
        return marshaler->QueryInterface(riid, ppv);

    return inner->QueryInterface(riid, ppv);
}
