#include "stdafx.h"

#include "FolderProxy.h"

#include "NativeFolderOps.h"

#include "NativeExtractIcon.h"

#include "NativeContextMenu.h"
#include "NativeDataObject.h"
#include "NativeDropTarget.h"

#include "ShellTrace.h"

#include "ViewCallback.h"



#include <vector>



void CXboxFolderProxy::SetFolderPidl(LPCITEMIDLIST pidl)
{
    m_folderPidl.Free();
    AttachShellPidl(m_folderPidl, pidl);
}



HRESULT CXboxFolderProxy::EnsureInner()

{
    XB_TRACE_SCOPE("EnsureInner");

    if (m_inner)
    {
        __xbTraceScope.Note("already loaded");
        return S_OK;
    }



    CComCritSecLock<CComCriticalSection> lock(m_innerLock);

    if (m_inner)
    {
        __xbTraceScope.Note("already loaded after lock");
        return S_OK;
    }

    __xbTraceScope.Note("loading managed CC45 root=%d", m_isNamespaceRoot ? 1 : 0);



    RH(CreateManagedFolder(IID_PPV_ARGS(&m_inner)));

    RH(m_inner->QueryInterface(IID_PPV_ARGS(&m_innerBase)));



    if (m_folderPidl)

    {

        CComPtr<IPersistFolder> persist;

        RH(m_inner->QueryInterface(IID_PPV_ARGS(&persist)));

        RH(persist->Initialize(m_folderPidl));

    }



    return S_OK;

}



HRESULT CXboxFolderProxy::GetThisIdList(LPITEMIDLIST* ppidl)

{

    if (!ppidl)

        return E_POINTER;



    *ppidl = NativeFolderOps::CreateNamespaceRelativePidl(m_folderPidl);

    return *ppidl ? S_OK : E_OUTOFMEMORY;

}



HRESULT CXboxFolderProxy::GetCurFolderAbsolute(LPITEMIDLIST* ppidl)

{

    if (!ppidl)

        return E_POINTER;



    *ppidl = CloneShellPidl(reinterpret_cast<PCUIDLIST_RELATIVE>(static_cast<LPITEMIDLIST>(m_folderPidl)));

    return *ppidl ? S_OK : E_OUTOFMEMORY;

}



HRESULT CXboxFolderProxy::GetDefItemCount(LPINT count)

{

    if (!count)

        return E_POINTER;



    if (UsesNativeRootListing())

    {

        UINT childCount = 0;

        const HRESULT hr = NativeFolderOps::GetRootChildCount(SHCONTF_FOLDERS | SHCONTF_NONFOLDERS, &childCount);

        *count = SUCCEEDED(hr) && childCount > 0 ? static_cast<int>(childCount) : 1;

        return S_OK;

    }



    RH(EnsureInner());



    CComPtr<IEnumIDList> enumerator;

    RH(m_innerBase->EnumObjects(nullptr, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS | SHCONTF_INCLUDEHIDDEN, &enumerator));



    UINT total = 0;

    for (;;)

    {

        CComHeapPtr<ITEMIDLIST> pidl;

        ULONG fetched = 0;

        const HRESULT hr = enumerator->Next(1, &pidl.m_pData, &fetched);

        if (hr != S_OK || fetched != 1)

            break;

        ++total;

    }



    *count = total > 0 ? static_cast<int>(total) : 1;

    return S_OK;

}



void CXboxFolderProxy::AttachViewCallback(IShellFolderViewCB* callback)

{

    m_viewCallback = callback;

}



HRESULT CXboxFolderProxy::QueryViewCallback(REFIID riid, void** ppv)

{

    if (!ppv)

        return E_POINTER;

    *ppv = nullptr;

    if (!m_viewCallback)

        return E_NOINTERFACE;

    return m_viewCallback->QueryInterface(riid, ppv);

}



STDMETHODIMP CXboxFolderProxy::GetClassID(CLSID* pClassID)

{

    if (!pClassID)

        return E_POINTER;

    *pClassID = CLSID_XboxFolder;

    return S_OK;

}



STDMETHODIMP CXboxFolderProxy::Initialize(LPCITEMIDLIST pidl)

{
    XB_TRACE_SCOPE("Initialize");

    SetFolderPidl(pidl);

    SetNamespaceRoot(true);

    return S_OK;

}



STDMETHODIMP CXboxFolderProxy::GetCurFolder(LPITEMIDLIST* ppidl)

{

    return GetCurFolderAbsolute(ppidl);

}



STDMETHODIMP CXboxFolderProxy::ParseDisplayName(HWND hwnd, LPBC pbc, LPWSTR pszDisplayName, ULONG* pchEaten, LPITEMIDLIST* ppidl, ULONG* pdwAttributes)

{
    XB_TRACE_SCOPE("ParseDisplayName");

    if (UsesNativeRootListing())

        return NativeFolderOps::ParseRootDisplayName(pszDisplayName, pchEaten, ppidl, pdwAttributes);



    RH(EnsureInner());

    return m_innerBase->ParseDisplayName(hwnd, pbc, pszDisplayName, pchEaten, ppidl, pdwAttributes);

}



STDMETHODIMP CXboxFolderProxy::EnumObjects(HWND hwnd, DWORD grfFlags, IEnumIDList** ppenumIDList)

{
    XB_TRACE_SCOPE("EnumObjects");
    __xbTraceScope.Note("root=%d grfFlags=0x%X", m_isNamespaceRoot ? 1 : 0, grfFlags);

    if (!ppenumIDList)

        return E_POINTER;



    *ppenumIDList = nullptr;



    if (UsesNativeRootListing())

    {

        std::vector<LPITEMIDLIST> pidls;

        RH(NativeFolderOps::EnumRootObjects(grfFlags, pidls));

        return NativeFolderOps::CreateNativeEnumIdList(pidls, ppenumIDList);

    }



    if (UsesNativeFolderOps())

    {

        if (m_isNamespaceRoot)

        {

            // DefView validates folder children during root view init; do not load managed here.

            std::vector<LPITEMIDLIST> pidls;

            return NativeFolderOps::CreateNativeEnumIdList(pidls, ppenumIDList);

        }



        RH(EnsureInner());

        return m_innerBase->EnumObjects(hwnd, grfFlags, ppenumIDList);

    }



    RH(EnsureInner());

    return m_innerBase->EnumObjects(hwnd, grfFlags, ppenumIDList);

}



STDMETHODIMP CXboxFolderProxy::BindToObject(LPCITEMIDLIST pidl, LPBC pbc, REFIID riid, void** ppv)

{
    XB_TRACE_SCOPE("BindToObject");
    if (pidl && pidl->mkid.cb)
        __xbTraceScope.Note("segment=%s native=%d", reinterpret_cast<LPCSTR>(pidl->mkid.abID), UsesNativeFolderOps() ? 1 : 0);

    if (!ppv)

        return E_POINTER;

    *ppv = nullptr;



    // Root listing binds resolve consoles from our own enumeration, so they are
    // always valid; create the native child proxy without further validation.
    if (UsesNativeRootListing())
    {
        CComHeapPtr<ITEMIDLIST> combined;
        AttachShellPidl(combined, m_folderPidl, pidl);
        if (!combined)
            return E_OUTOFMEMORY;

        CComObject<CXboxFolderProxy>* proxy = nullptr;
        RH(CComObject<CXboxFolderProxy>::CreateInstance(&proxy));
        proxy->SetFolderPidl(combined);
        proxy->SetNamespaceRoot(false);
        return proxy->QueryInterface(riid, ppv);
    }

    // Deeper (managed-backed) folders can receive bogus binds from Explorer during
    // context-menu/icon resolution; reject implausible child segments here.
    if (pidl && pidl->mkid.cb)
    {
        if (!NativeFolderOps::IsPlausibleChildBind(m_folderPidl, pidl))
            return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    RH(EnsureInner());



    CComPtr<IShellFolder> child;

    RH(m_innerBase->BindToObject(pidl, pbc, IID_PPV_ARGS(&child)));



    CComObject<CXboxFolderProxy>* proxy = nullptr;

    RH(CComObject<CXboxFolderProxy>::CreateInstance(&proxy));



    CComPtr<IShellFolder2> childFolder2;

    RH(child->QueryInterface(IID_PPV_ARGS(&childFolder2)));

    proxy->SetInner(childFolder2);



    CComPtr<IPersistFolder2> childPersist;

    if (SUCCEEDED(child->QueryInterface(IID_PPV_ARGS(&childPersist))))

    {

        LPITEMIDLIST childPidl = nullptr;

        if (SUCCEEDED(childPersist->GetCurFolder(&childPidl)) && childPidl)

        {

            proxy->SetFolderPidl(childPidl);

            CoTaskMemFree(childPidl);

        }

    }



    return proxy->QueryInterface(riid, ppv);

}



STDMETHODIMP CXboxFolderProxy::BindToStorage(LPCITEMIDLIST pidl, LPBC pbc, REFIID riid, void** ppv)

{

    if (UsesNativeFolderOps())

        return E_NOTIMPL;



    RH(EnsureInner());

    return m_innerBase->BindToStorage(pidl, pbc, riid, ppv);

}



STDMETHODIMP CXboxFolderProxy::CompareIDs(LPARAM lParam, LPCITEMIDLIST pidl1, LPCITEMIDLIST pidl2)

{

    if (UsesNativeFolderOps())

        return NativeFolderOps::CompareSimplePidls(pidl1, pidl2);



    RH(EnsureInner());

    return m_innerBase->CompareIDs(lParam, pidl1, pidl2);

}



STDMETHODIMP CXboxFolderProxy::CreateViewObject(HWND hwndOwner, REFIID riid, void** ppv)

{
    XB_TRACE_SCOPE("CreateViewObject");
    ShellTraceGuid("CreateViewObject", riid);
    __xbTraceScope.Note("root=%d native=%d", m_isNamespaceRoot ? 1 : 0, UsesNativeFolderOps() ? 1 : 0);

    if (!ppv)

        return E_POINTER;

    *ppv = nullptr;



    if (riid == IID_IShellView || riid == IID_IShellView2)
    {
        // Keep the namespace root on the native listing path. Loading managed CC45 here
        // flips UsesNativeFolderOps() off and breaks console double-click navigation.
        if (!UsesNativeRootListing())
            RH(EnsureInner());

        return CXboxViewCallback::CreateShellView(this, hwndOwner, riid, ppv);
    }

    if (riid == IID_IShellFolderViewCB)
        return CXboxViewCallback::CreateFolderViewCallback(this, hwndOwner, riid, ppv);



    if (UsesNativeFolderOps())
    {
        if (riid == IID_IDropTarget)
            return CreateNativeDropTarget(m_folderPidl, 0, nullptr, hwndOwner, riid, ppv);

        // Route through our own GetUIObjectOf so the folder background context menu
        // (and other UI objects) use the native implementation. Forwarding to the
        // managed CreateViewObject hands DefView the managed CCW menu, which crashes.
        return GetUIObjectOf(hwndOwner, 0, nullptr, riid, nullptr, ppv);
    }



    RH(EnsureInner());

    if (riid == IID_IDropTarget)
        return CreateNativeDropTarget(m_folderPidl, 0, nullptr, hwndOwner, riid, ppv);

    return m_innerBase->GetUIObjectOf(hwndOwner, 0, nullptr, riid, nullptr, ppv);

}



STDMETHODIMP CXboxFolderProxy::GetAttributesOf(UINT cidl, LPCITEMIDLIST* apidl, ULONG* rgfInOut)

{

    if (!rgfInOut)

        return E_POINTER;



    if (UsesNativeFolderOps())
    {
        if (cidl == 0)
        {
            if (UsesNativeRootListing())
                *rgfInOut &= NativeFolderOps::kRootAttributes;
            else
                *rgfInOut &= (NativeFolderOps::kConsoleAttributes | NativeFolderOps::kSfgaoFolder);

            return S_OK;
        }

        if (UsesNativeRootListing())
        {
            if (!apidl)
                return E_INVALIDARG;

            ULONG common = 0xFFFFFFFF;
            for (UINT i = 0; i < cidl; ++i)
            {
                const auto segment = NativeFolderOps::GetLastSegment(apidl[i]);
                common &= NativeFolderOps::GetChildAttributes(segment.c_str());
            }

            *rgfInOut &= common;
            return S_OK;
        }
    }

    RH(EnsureInner());

    return m_innerBase->GetAttributesOf(cidl, apidl, rgfInOut);

}



STDMETHODIMP CXboxFolderProxy::GetUIObjectOf(HWND hwndOwner, UINT cidl, LPCITEMIDLIST* apidl, REFIID riid, UINT* rgfInOut, void** ppv)

{
    XB_TRACE_SCOPE("GetUIObjectOf");
    ShellTraceGuid("GetUIObjectOf", riid);
    __xbTraceScope.Note("cidl=%u native=%d", cidl, UsesNativeFolderOps() ? 1 : 0);

    if (riid == IID_IExtractIcon || riid == IID_IExtractIconA || riid == IID_IExtractIconW)

        return CreateNativeExtractIcon(m_folderPidl, cidl, apidl, riid, ppv);



    // Always use the native context menu. The managed CCW menu crashes Explorer
    // when DefView hosts the managed IShellFolder directly (CreateShellFolderView
    // passes CC45 as pshf). Add Xbox still launches managed UI on a dedicated STA
    // thread from native InvokeCommand.
    if (riid == IID_IContextMenu || riid == IID_IContextMenu2 || riid == IID_IContextMenu3)
        return CreateNativeContextMenu(m_folderPidl, cidl, apidl, riid, ppv);



    if (riid == IID_IDataObject)
    {
        if (cidl == 0)
            return E_INVALIDARG;

        return CreateNativeDataObject(m_folderPidl, cidl, apidl, riid, ppv);
    }

    if (riid == IID_IDropTarget)
        return CreateNativeDropTarget(m_folderPidl, cidl, apidl, hwndOwner, riid, ppv);

    if (UsesNativeFolderOps())
    {
        return E_NOINTERFACE;
    }



    RH(EnsureInner());

    return m_innerBase->GetUIObjectOf(hwndOwner, cidl, apidl, riid, rgfInOut, ppv);

}



STDMETHODIMP CXboxFolderProxy::GetDisplayNameOf(LPCITEMIDLIST pidl, DWORD uFlags, STRRET* pName)

{

    if (UsesNativeRootListing())

        return NativeFolderOps::GetDisplayName(pidl, uFlags, pName);



    RH(EnsureInner());

    return m_innerBase->GetDisplayNameOf(pidl, uFlags, pName);

}



STDMETHODIMP CXboxFolderProxy::SetNameOf(HWND hwnd, LPCITEMIDLIST pidl, LPCWSTR pszName, DWORD uFlags, LPITEMIDLIST* ppidlOut)

{

    RH(EnsureInner());

    return m_innerBase->SetNameOf(hwnd, pidl, pszName, uFlags, ppidlOut);

}



STDMETHODIMP CXboxFolderProxy::GetDefaultSearchGUID(GUID* pguid)

{

    if (!pguid)

        return E_POINTER;

    *pguid = GUID_NULL;

    return E_NOTIMPL;

}



STDMETHODIMP CXboxFolderProxy::EnumSearches(IEnumExtraSearch** ppenum)

{

    if (ppenum)

        *ppenum = nullptr;

    return E_NOTIMPL;

}



STDMETHODIMP CXboxFolderProxy::GetDefaultColumn(DWORD dwRes, ULONG* pSort, ULONG* pDisplay)

{

    if (!pSort || !pDisplay)

        return E_POINTER;

    *pSort = 0;

    *pDisplay = 0;

    return S_OK;

}



STDMETHODIMP CXboxFolderProxy::GetDefaultColumnState(UINT iColumn, SHCOLSTATEF* pcsFlags)

{

    if (!pcsFlags)

        return E_POINTER;

    if (iColumn != 0)

        return E_INVALIDARG;

    *pcsFlags = SHCOLSTATE_TYPE_STR | SHCOLSTATE_ONBYDEFAULT;

    return S_OK;

}



STDMETHODIMP CXboxFolderProxy::GetDetailsEx(LPCITEMIDLIST pidl, const PROPERTYKEY* pscid, VARIANT* pv)

{

    return E_NOTIMPL;

}



STDMETHODIMP CXboxFolderProxy::GetDetailsOf(LPCITEMIDLIST pidl, UINT iColumn, SHELLDETAILS* psd)

{

    if (UsesNativeFolderOps())

        return NativeFolderOps::GetDetailsOf(pidl, iColumn, psd);



    RH(EnsureInner());

    return m_inner->GetDetailsOf(pidl, iColumn, psd);

}



STDMETHODIMP CXboxFolderProxy::MapColumnToSCID(UINT iColumn, PROPERTYKEY* pscid)

{

    if (!pscid)

        return E_POINTER;

    if (iColumn != 0)

        return E_INVALIDARG;



    pscid->fmtid = { 0xB725F130, 0x47EF, 0x101A, { 0xA5, 0xF1, 0x02, 0x60, 0x8C, 0x9E, 0xBA, 0xCC } };

    pscid->pid = 0x0A;

    return S_OK;

}


