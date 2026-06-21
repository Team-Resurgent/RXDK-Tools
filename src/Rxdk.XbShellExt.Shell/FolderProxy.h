#pragma once

#include "resource.h"

class ATL_NO_VTABLE CXboxFolderProxy :
    public CComObjectRootEx<CComMultiThreadModel>,
    public CComCoClass<CXboxFolderProxy, &CLSID_XboxFolder>,
    public IShellFolder2,
    public IPersistFolder2
{
public:
    DECLARE_REGISTRY_RESOURCEID(IDR_SHELLFOLDER)
    BEGIN_COM_MAP(CXboxFolderProxy)
        COM_INTERFACE_ENTRY(IPersistFolder2)
        COM_INTERFACE_ENTRY2(IPersistFolder, IPersistFolder2)
        COM_INTERFACE_ENTRY2(IPersist, IPersistFolder2)
        COM_INTERFACE_ENTRY(IShellFolder2)
        COM_INTERFACE_ENTRY2(IShellFolder, IShellFolder2)
    END_COM_MAP()

    HRESULT FinalConstruct() { return m_innerLock.Init(); }
    void FinalRelease() { m_innerLock.Term(); }

    void SetInner(IShellFolder2* inner)
    {
        m_inner = inner;
        m_innerBase.Release();
        if (inner)
            inner->QueryInterface(IID_PPV_ARGS(&m_innerBase));
    }

    void SetFolderPidl(LPCITEMIDLIST pidl);

    void SetNamespaceRoot(bool isRoot) { m_isNamespaceRoot = isRoot; }

    bool UsesNativeFolderOps() const { return m_inner == nullptr; }

    bool UsesNativeRootListing() const { return m_inner == nullptr && m_isNamespaceRoot; }

    HRESULT EnsureInner();

    HRESULT GetThisIdList(LPITEMIDLIST* ppidl);
    HRESULT GetCurFolderAbsolute(LPITEMIDLIST* ppidl);
    HRESULT GetDefItemCount(LPINT count);

    void AttachViewCallback(IShellFolderViewCB* callback);
    HRESULT QueryViewCallback(REFIID riid, void** ppv);

    STDMETHOD(GetClassID)(CLSID* pClassID);
    STDMETHOD(Initialize)(LPCITEMIDLIST pidl);
    STDMETHOD(GetCurFolder)(LPITEMIDLIST* ppidl);

    STDMETHOD(ParseDisplayName)(HWND hwnd, LPBC pbc, LPWSTR pszDisplayName, ULONG* pchEaten, LPITEMIDLIST* ppidl, ULONG* pdwAttributes);
    STDMETHOD(EnumObjects)(HWND hwnd, DWORD grfFlags, IEnumIDList** ppenumIDList);
    STDMETHOD(BindToObject)(LPCITEMIDLIST pidl, LPBC pbc, REFIID riid, void** ppv);
    STDMETHOD(BindToStorage)(LPCITEMIDLIST pidl, LPBC pbc, REFIID riid, void** ppv);
    STDMETHOD(CompareIDs)(LPARAM lParam, LPCITEMIDLIST pidl1, LPCITEMIDLIST pidl2);
    STDMETHOD(CreateViewObject)(HWND hwndOwner, REFIID riid, void** ppv);
    STDMETHOD(GetAttributesOf)(UINT cidl, LPCITEMIDLIST* apidl, ULONG* rgfInOut);
    STDMETHOD(GetUIObjectOf)(HWND hwndOwner, UINT cidl, LPCITEMIDLIST* apidl, REFIID riid, UINT* rgfInOut, void** ppv);
    STDMETHOD(GetDisplayNameOf)(LPCITEMIDLIST pidl, DWORD uFlags, STRRET* pName);
    STDMETHOD(SetNameOf)(HWND hwnd, LPCITEMIDLIST pidl, LPCWSTR pszName, DWORD uFlags, LPITEMIDLIST* ppidlOut);

    STDMETHOD(GetDefaultSearchGUID)(GUID* pguid);
    STDMETHOD(EnumSearches)(IEnumExtraSearch** ppenum);
    STDMETHOD(GetDefaultColumn)(DWORD dwRes, ULONG* pSort, ULONG* pDisplay);
    STDMETHOD(GetDefaultColumnState)(UINT iColumn, SHCOLSTATEF* pcsFlags);
    STDMETHOD(GetDetailsEx)(LPCITEMIDLIST pidl, const PROPERTYKEY* pscid, VARIANT* pv);
    STDMETHOD(GetDetailsOf)(LPCITEMIDLIST pidl, UINT iColumn, SHELLDETAILS* psd);
    STDMETHOD(MapColumnToSCID)(UINT iColumn, PROPERTYKEY* pscid);

private:
    CComPtr<IShellFolder2> m_inner;
    // Flat base IShellFolder pointer for the managed CCW. Inherited IShellFolder
    // methods called through the derived IShellFolder2 vtable do not dispatch into
    // the managed implementation; calling them through a separately-QI'd flat base
    // interface does (same as IPersistFolder). Use this for all IShellFolder calls.
    CComPtr<IShellFolder> m_innerBase;
    CComHeapPtr<ITEMIDLIST> m_folderPidl;
    CComPtr<IShellFolderViewCB> m_viewCallback;
    CComCriticalSection m_innerLock;
    bool m_isNamespaceRoot = false;
};
