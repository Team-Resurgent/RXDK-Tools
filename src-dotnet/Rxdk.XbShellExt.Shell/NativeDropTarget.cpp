#include "stdafx.h"
#include "NativeDropTarget.h"
#include "NativeShellNotify.h"
#include "NativeUi.h"
#include "ShellTrace.h"

namespace
{
    bool SupportsPasteFormat(IDataObject* dataObject)
    {
        if (!dataObject)
            return false;

        static const CLIPFORMAT cfXbox =
            static_cast<CLIPFORMAT>(RegisterClipboardFormatA("XBOX_FILEDESCRIPTOR"));
        static const CLIPFORMAT cfFileDescW =
            static_cast<CLIPFORMAT>(RegisterClipboardFormatW(CFSTR_FILEDESCRIPTORW));

        const CLIPFORMAT formats[] = { CF_HDROP, cfXbox, cfFileDescW };
        for (const CLIPFORMAT cf : formats)
        {
            FORMATETC etc = {};
            etc.cfFormat = cf;
            etc.dwAspect = DVASPECT_CONTENT;
            etc.lindex = -1;
            etc.tymed = TYMED_HGLOBAL;
            if (dataObject->QueryGetData(&etc) == S_OK)
                return true;
        }

        return false;
    }

    DWORD ChooseDropEffect(DWORD grfKeyState, DWORD allowed)
    {
        if ((grfKeyState & MK_LBUTTON) == 0)
            return DROPEFFECT_NONE;

        if (grfKeyState & MK_CONTROL)
            return (allowed & DROPEFFECT_COPY) ? DROPEFFECT_COPY : DROPEFFECT_NONE;

        if (grfKeyState & MK_SHIFT)
            return (allowed & DROPEFFECT_MOVE) ? DROPEFFECT_MOVE : DROPEFFECT_NONE;

        if (allowed & DROPEFFECT_MOVE)
            return DROPEFFECT_MOVE;

        if (allowed & DROPEFFECT_COPY)
            return DROPEFFECT_COPY;

        return DROPEFFECT_NONE;
    }

    class ATL_NO_VTABLE CNativeDropTarget :
        public CComObjectRootEx<CComMultiThreadModel>,
        public IDropTarget
    {
    public:
        BEGIN_COM_MAP(CNativeDropTarget)
            COM_INTERFACE_ENTRY(IDropTarget)
        END_COM_MAP()

        void Initialize(LPCITEMIDLIST folderPidl, LPCITEMIDLIST childPidl, HWND hwnd)
        {
            m_hwnd = hwnd;
            m_folderPidl.Free();
            m_childPidl.Free();
            if (folderPidl)
                AttachShellPidl(m_folderPidl, folderPidl);
            if (childPidl)
                AttachShellPidl(m_childPidl, childPidl);
        }

        STDMETHOD(DragEnter)(IDataObject* pDataObj, DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
        {
            UNREFERENCED_PARAMETER(pt);
            if (!pdwEffect)
                return E_POINTER;

            *pdwEffect = DROPEFFECT_NONE;
            if (!SupportsPasteFormat(pDataObj))
                return S_OK;

            m_grfKeyState = grfKeyState & ~MK_RBUTTON;
            *pdwEffect = ChooseDropEffect(m_grfKeyState, DROPEFFECT_COPY | DROPEFFECT_MOVE);
            if (*pdwEffect == DROPEFFECT_NONE)
                *pdwEffect = DROPEFFECT_COPY;
            return S_OK;
        }

        STDMETHOD(DragOver)(DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
        {
            UNREFERENCED_PARAMETER(pt);
            if (!pdwEffect)
                return E_POINTER;

            m_grfKeyState = grfKeyState & ~MK_RBUTTON;
            *pdwEffect = ChooseDropEffect(m_grfKeyState, DROPEFFECT_COPY | DROPEFFECT_MOVE);
            if (*pdwEffect == DROPEFFECT_NONE)
                *pdwEffect = DROPEFFECT_COPY;
            return S_OK;
        }

        STDMETHOD(DragLeave)()
        {
            m_grfKeyState = 0;
            return S_OK;
        }

        STDMETHOD(Drop)(IDataObject* pDataObj, DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
        {
            UNREFERENCED_PARAMETER(pt);
            XB_TRACE_SCOPE("NativeDropTarget.Drop");
            if (!pdwEffect)
                return E_POINTER;

            *pdwEffect = DROPEFFECT_NONE;
            if (!pDataObj || !SupportsPasteFormat(pDataObj))
                return S_OK;

            m_grfKeyState = grfKeyState & ~MK_RBUTTON;
            DWORD effect = ChooseDropEffect(m_grfKeyState, DROPEFFECT_COPY | DROPEFFECT_MOVE);
            if (effect == DROPEFFECT_NONE)
                effect = DROPEFFECT_COPY;

            CComPtr<IShellFolder> managed;
            HRESULT hr = CreateManagedFolder(IID_PPV_ARGS(&managed));
            if (FAILED(hr) || !managed)
            {
                __xbTraceScope.Note("CreateManagedFolder hr=0x%08X", hr);
                return hr;
            }

            if (m_folderPidl)
            {
                CComPtr<IPersistFolder> persist;
                if (SUCCEEDED(managed->QueryInterface(IID_PPV_ARGS(&persist))) && persist)
                    persist->Initialize(m_folderPidl);
            }

            CComPtr<IXboxShellExtUi> ui;
            hr = managed->QueryInterface(IID_PPV_ARGS(&ui));
            if (FAILED(hr) || !ui)
            {
                __xbTraceScope.Note("QueryInterface IXboxShellExtUi hr=0x%08X", hr);
                return hr;
            }

            hr = ui->PerformDrop(m_hwnd, m_childPidl, pDataObj, &effect);
            __xbTraceScope.Note("PerformDrop hr=0x%08X effect=0x%X", hr, effect);
            if (SUCCEEDED(hr))
            {
                *pdwEffect = effect;
                NotifyFolderContentsChanged(m_folderPidl, m_childPidl);
            }
            return hr;
        }

    private:
        HWND m_hwnd = nullptr;
        CComHeapPtr<ITEMIDLIST> m_folderPidl;
        CComHeapPtr<ITEMIDLIST> m_childPidl;
        DWORD m_grfKeyState = 0;
    };
}

HRESULT CreateNativeDropTarget(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    HWND hwndOwner,
    REFIID riid,
    void** ppv)
{
    XB_TRACE_SCOPE("CreateNativeDropTarget");
    UNREFERENCED_PARAMETER(cidl);
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;
    if (riid != IID_IDropTarget)
        return E_NOINTERFACE;

    LPCITEMIDLIST childPidl = (apidl && cidl > 0) ? apidl[0] : nullptr;

    CComObject<CNativeDropTarget>* target = nullptr;
    RH(CComObject<CNativeDropTarget>::CreateInstance(&target));
    target->Initialize(folderPidl, childPidl, hwndOwner);
    return target->QueryInterface(riid, ppv);
}
