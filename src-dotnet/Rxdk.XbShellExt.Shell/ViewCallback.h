#pragma once

#include "ShellObjectSite.h"

class CXboxFolderProxy;

class ATL_NO_VTABLE CXboxViewCallback :
    public CComObjectRootEx<CComMultiThreadModel>,
    public CShellObjectWithSite,
    public IShellFolderViewCB
{
public:
    BEGIN_COM_MAP(CXboxViewCallback)
        COM_INTERFACE_ENTRY(IObjectWithSite)
        COM_INTERFACE_ENTRY(IShellFolderViewCB)
    END_COM_MAP()

    static HRESULT CreateShellView(CXboxFolderProxy* folder, HWND hwnd, REFIID riid, void** ppv);
    static HRESULT CreateFolderViewCallback(CXboxFolderProxy* folder, HWND hwnd, REFIID riid, void** ppv);

    STDMETHOD(MessageSFVCB)(UINT uMsg, WPARAM wParam, LPARAM lParam);

    void Initialize(CXboxFolderProxy* folder, HWND hwnd)
    {
        m_folder = folder;
        m_hwnd = hwnd;
    }

private:
    CXboxFolderProxy* m_folder = nullptr;
    HWND m_hwnd = nullptr;
};
