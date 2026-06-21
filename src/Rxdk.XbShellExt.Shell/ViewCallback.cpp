#include "stdafx.h"

#include "FolderProxy.h"

#include "ViewCallback.h"

#include "NativeFolderOps.h"
#include "ShellTrace.h"

namespace
{
    MIDL_INTERFACE("3D8B0590-F691-11d2-8EA9-006097DF5BD4")
    IAsyncOperation : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE SetAsyncMode(BOOL fDoOpAsync) = 0;
        virtual HRESULT STDMETHODCALLTYPE GetAsyncMode(BOOL* pfIsOpAsync) = 0;
        virtual HRESULT STDMETHODCALLTYPE StartOperation(IBindCtx* pbcReserved) = 0;
        virtual HRESULT STDMETHODCALLTYPE InOperation(BOOL* pfInAsyncOp) = 0;
        virtual HRESULT STDMETHODCALLTYPE EndOperation(HRESULT hResult, IBindCtx* pbcReserved, DWORD dwEffects) = 0;
    };

    constexpr UINT kDvmBackgroundEnum = 32;
    constexpr UINT kDvmInitMenuPopup = 7;
    constexpr UINT kDvmRelease = 12;
    constexpr UINT kDvmWindowCreated = 15;
    constexpr UINT kSfvmBackgroundEnumDone = 48;
    constexpr UINT kSfvmGetNotify = 49;
    constexpr UINT kSfvmDontCustomize = 56;
    constexpr UINT kSfvmGetZone = 58;
    constexpr UINT kSfvmThisIdList = 41;
    constexpr UINT kSfvmGetDetailsOf = 23;
    constexpr UINT kSfvmDefItemCount = 26;
    constexpr UINT kSfvmDidDragDrop = 36;
    constexpr UINT kSfvmUpdateStatusBar = 31;
    constexpr UINT kSfvmGetHelpText = 3;
    constexpr UINT kSfvmGetPane = 59;
    constexpr UINT kSfvmFsNotify = 14;
    constexpr UINT kSfvmGetHelpTextW = 65;

    constexpr LONG kNotifyEvents =
        SHCNE_DISKEVENTS |
        SHCNE_ASSOCCHANGED |
        SHCNE_RMDIR |
        SHCNE_DELETE |
        SHCNE_MKDIR |
        SHCNE_CREATE |
        SHCNE_RENAMEFOLDER |
        SHCNE_RENAMEITEM |
        SHCNE_UPDATEITEM |
        SHCNE_ATTRIBUTES;

    struct ShellDetailsInfo
    {
        PCUITEMID_CHILD pidl;
        SHELLDETAILS details;
        int iImage;
    };

    std::wstring GetLastPidlSegment(LPCITEMIDLIST pidl)
    {
        if (!pidl || !pidl->mkid.cb)
            return L"";

        std::string last(reinterpret_cast<LPCSTR>(pidl->mkid.abID));
        if (!last.empty() && last[0] == '?')
            last.erase(0, 1);
        if (last.empty())
            return L"";

        const int wideLen = MultiByteToWideChar(CP_ACP, 0, last.c_str(), -1, nullptr, 0);
        if (wideLen <= 0)
            return L"";

        std::wstring wide(static_cast<size_t>(wideLen - 1), L'\0');
        MultiByteToWideChar(CP_ACP, 0, last.c_str(), -1, wide.data(), wideLen);
        return wide;
    }

    HRESULT SetShellDetailsString(SHELLDETAILS* psd, LPCWSTR text)
    {
        psd->fmt = LVCFMT_LEFT;
        psd->cxChar = 30;
        psd->str.uType = STRRET_WSTR;
        const size_t chars = wcslen(text) + 1;
        auto* buffer = static_cast<WCHAR*>(CoTaskMemAlloc(chars * sizeof(WCHAR)));
        if (!buffer)
            return E_OUTOFMEMORY;
        StringCchCopyW(buffer, chars, text);
        psd->str.pOleStr = buffer;
        return S_OK;
    }

    HRESULT OnGetDetailsOf(UINT column, ShellDetailsInfo* info, CXboxFolderProxy* folder)
    {
        if (!info || !folder || column != 0)
            return E_NOTIMPL;

        if (!info->pidl)
            return SetShellDetailsString(&info->details, L"Name");

        if (folder->UsesNativeRootListing())
        {
            const auto segment = GetLastPidlSegment(info->pidl);
            return SetShellDetailsString(&info->details, segment.empty() ? L"" : segment.c_str());
        }

        RH(folder->EnsureInner());
        return folder->GetDetailsOf(info->pidl, column, &info->details);
    }

    HRESULT OnDefItemCount(LPINT count, CXboxFolderProxy* folder)
    {
        if (!count || !folder)
            return E_POINTER;

        return folder->GetDefItemCount(count);
    }

    HRESULT OnDidDragDrop(DWORD dropEffect, IDataObject* dataObject)
    {
        if (dropEffect != DROPEFFECT_MOVE || !dataObject)
            return S_OK;

        CComPtr<IAsyncOperation> asyncOperation;
        if (FAILED(dataObject->QueryInterface(IID_PPV_ARGS(&asyncOperation))))
            return S_FALSE;

        BOOL inAsyncOp = TRUE;
        if (SUCCEEDED(asyncOperation->InOperation(&inAsyncOp)) && inAsyncOp)
            return S_OK;

        return S_FALSE;
    }
}

HRESULT CXboxViewCallback::CreateShellView(CXboxFolderProxy* folder, HWND hwnd, REFIID riid, void** ppv)
{
    XB_TRACE_SCOPE("CreateShellView");
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;
    if (!folder)
        return E_INVALIDARG;

    CComObject<CXboxViewCallback>* callback = nullptr;
    RH(CComObject<CXboxViewCallback>::CreateInstance(&callback));
    callback->Initialize(folder, hwnd);

    CComPtr<IShellFolderViewCB> viewCallback;
    RH(callback->QueryInterface(IID_PPV_ARGS(&viewCallback)));

    CComPtr<IShellFolder> shellFolder;
    RH(folder->QueryInterface(IID_PPV_ARGS(&shellFolder)));

    SFV_CREATE sfvCreate = {};
    sfvCreate.cbSize = sizeof(SFV_CREATE);
    sfvCreate.pshf = shellFolder;
    sfvCreate.psvOuter = nullptr;
    sfvCreate.psfvcb = viewCallback;

    CComPtr<IShellView> shellView;
    RH(XdkCreateShellFolderView(&sfvCreate, &shellView));
    folder->AttachViewCallback(viewCallback);
    return shellView->QueryInterface(riid, ppv);
}

HRESULT CXboxViewCallback::CreateFolderViewCallback(CXboxFolderProxy* folder, HWND hwnd, REFIID riid, void** ppv)
{
    XB_TRACE_SCOPE("CreateFolderViewCallback");
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;
    if (!folder || riid != IID_IShellFolderViewCB)
        return E_NOINTERFACE;

    if (SUCCEEDED(folder->QueryViewCallback(riid, ppv)))
        return S_OK;

    CComObject<CXboxViewCallback>* callback = nullptr;
    RH(CComObject<CXboxViewCallback>::CreateInstance(&callback));
    callback->Initialize(folder, hwnd);

    CComPtr<IShellFolderViewCB> viewCallback;
    RH(callback->QueryInterface(IID_PPV_ARGS(&viewCallback)));
    folder->AttachViewCallback(viewCallback);
    return viewCallback->QueryInterface(riid, ppv);
}

STDMETHODIMP CXboxViewCallback::MessageSFVCB(UINT uMsg, WPARAM wParam, LPARAM lParam)
{
    ShellTraceMsg("MessageSFVCB", uMsg);
    switch (uMsg)
    {
    case kSfvmThisIdList:
    {
        if (!lParam)
            return E_INVALIDARG;
        auto** ppidl = reinterpret_cast<LPITEMIDLIST*>(lParam);
        return m_folder ? m_folder->GetThisIdList(ppidl) : E_FAIL;
    }
    case kSfvmGetNotify:
    {
        if (!wParam || !lParam)
            return E_INVALIDARG;
        auto** ppidl = reinterpret_cast<LPITEMIDLIST*>(wParam);
        *reinterpret_cast<LONG*>(lParam) = kNotifyEvents;
        return m_folder ? m_folder->GetCurFolderAbsolute(ppidl) : E_FAIL;
    }
    case kSfvmGetDetailsOf:
        return OnGetDetailsOf(static_cast<UINT>(wParam), reinterpret_cast<ShellDetailsInfo*>(lParam), m_folder);
    case kSfvmGetPane:
        return S_OK;
    case kSfvmFsNotify:
        return S_OK;
    case kSfvmDefItemCount:
        return OnDefItemCount(reinterpret_cast<LPINT>(lParam), m_folder);
    case kSfvmDidDragDrop:
        return OnDidDragDrop(static_cast<DWORD>(wParam), reinterpret_cast<IDataObject*>(lParam));
    case kSfvmUpdateStatusBar:
        return S_OK;
    case kSfvmGetHelpText:
    case kSfvmGetHelpTextW:
        if (lParam)
        {
            const int cchMax = static_cast<int>(HIWORD(wParam));
            if (cchMax > 0)
                StringCchCopyW(reinterpret_cast<LPWSTR>(lParam), cchMax, L"");
        }
        return S_OK;
    case kSfvmBackgroundEnumDone:
    case kDvmInitMenuPopup:
    case kDvmWindowCreated:
        return S_OK;
    case kDvmBackgroundEnum:
        // Match legacy xbshlext: decline DefView background enum thread.
        return S_OK;
    case kSfvmDontCustomize:
        if (lParam)
            *reinterpret_cast<BOOL*>(lParam) = FALSE;
        return S_OK;
    case kSfvmGetZone:
        if (!lParam)
            return E_INVALIDARG;
        *reinterpret_cast<DWORD*>(lParam) = URLZONE_LOCAL_MACHINE;
        return S_OK;
    case kDvmRelease:
    {
        auto* cb = reinterpret_cast<IShellFolderViewCB*>(lParam);
        if (cb)
            cb->Release();
        return S_OK;
    }
    default:
        return E_NOTIMPL;
    }
}
