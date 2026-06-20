#include "stdafx.h"
#include "NativeDataObject.h"
#include "NativeUi.h"
#include "ShellTrace.h"

#include <algorithm>
#include <vector>

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

    class ATL_NO_VTABLE CNativeEnumFormatEtc :
        public CComObjectRootEx<CComMultiThreadModel>,
        public IEnumFORMATETC
    {
    public:
        BEGIN_COM_MAP(CNativeEnumFormatEtc)
            COM_INTERFACE_ENTRY(IEnumFORMATETC)
        END_COM_MAP()

        void Initialize(const FORMATETC* formats, ULONG count)
        {
            m_formats.assign(formats, formats + count);
            m_index = 0;
        }

        STDMETHOD(Next)(ULONG celt, FORMATETC* rgelt, ULONG* pceltFetched)
        {
            if (!rgelt)
                return E_POINTER;

            ULONG fetched = 0;
            while (fetched < celt && m_index < m_formats.size())
                rgelt[fetched++] = m_formats[m_index++];

            if (pceltFetched)
                *pceltFetched = fetched;

            return fetched == 0 ? S_FALSE : S_OK;
        }

        STDMETHOD(Skip)(ULONG celt)
        {
            m_index = std::min<ULONG>(m_index + celt, static_cast<ULONG>(m_formats.size()));
            return m_index >= m_formats.size() ? S_FALSE : S_OK;
        }

        STDMETHOD(Reset)()
        {
            m_index = 0;
            return S_OK;
        }

        STDMETHOD(Clone)(IEnumFORMATETC** ppenum)
        {
            if (!ppenum)
                return E_POINTER;

            *ppenum = nullptr;
            CComObject<CNativeEnumFormatEtc>* clone = nullptr;
            RH(CComObject<CNativeEnumFormatEtc>::CreateInstance(&clone));
            clone->Initialize(m_formats.data(), static_cast<ULONG>(m_formats.size()));
            clone->m_index = m_index;
            return clone->QueryInterface(IID_PPV_ARGS(ppenum));
        }

    private:
        std::vector<FORMATETC> m_formats;
        ULONG m_index = 0;
    };

    HRESULT EnsureManagedUi(
        LPCITEMIDLIST folderPidl,
        CComPtr<IXboxShellExtUi>& ui)
    {
        CComPtr<IShellFolder> managed;
        HRESULT hr = CreateManagedFolder(IID_PPV_ARGS(&managed));
        if (FAILED(hr))
            return hr;

        if (folderPidl)
        {
            CComPtr<IPersistFolder> persist;
            if (SUCCEEDED(managed->QueryInterface(IID_PPV_ARGS(&persist))) && persist)
                persist->Initialize(folderPidl);
        }

        return managed->QueryInterface(IID_PPV_ARGS(&ui));
    }

    HGLOBAL CloneGlobal(HGLOBAL source)
    {
        if (!source)
            return nullptr;

        const SIZE_T size = GlobalSize(source);
        if (size == 0)
            return nullptr;

        HGLOBAL clone = GlobalAlloc(GMEM_MOVEABLE, size);
        if (!clone)
            return nullptr;

        const void* src = GlobalLock(source);
        void* dst = GlobalLock(clone);
        if (!src || !dst)
        {
            GlobalUnlock(source);
            GlobalUnlock(clone);
            GlobalFree(clone);
            return nullptr;
        }

        memcpy(dst, src, size);
        GlobalUnlock(source);
        GlobalUnlock(clone);
        return clone;
    }

    class ATL_NO_VTABLE CNativeDataObject :
        public CComObjectRootEx<CComMultiThreadModel>,
        public IDataObject,
        public IAsyncOperation
    {
    public:
        BEGIN_COM_MAP(CNativeDataObject)
            COM_INTERFACE_ENTRY(IDataObject)
            COM_INTERFACE_ENTRY(IAsyncOperation)
        END_COM_MAP()

        HRESULT Initialize(LPCITEMIDLIST folderPidl, UINT cidl, LPCITEMIDLIST* apidl)
        {
            m_folderPidl.Free();
            m_childPidls.clear();
            m_ui.Release();
            m_preferredEffect = DROPEFFECT_COPY;
            m_asyncStarted = FALSE;

            if (folderPidl)
                AttachShellPidl(m_folderPidl, folderPidl);

            if (cidl > 0 && apidl)
            {
                // Reserve up front so the vector never reallocates while we add
                // elements. CComHeapPtr owns its pidl and transfers ownership when
                // copied; copying/moving it through the vector (e.g. push_back of a
                // local) leaves two owners and double-frees on destruction (heap
                // corruption). Default-construct in place and fill via AttachShellPidl
                // so the CComHeapPtr is never copied.
                m_childPidls.reserve(cidl);
                for (UINT i = 0; i < cidl; ++i)
                {
                    if (!apidl[i])
                        return E_INVALIDARG;
                    m_childPidls.emplace_back();
                    AttachShellPidl(m_childPidls.back(), apidl[i]);
                    if (!m_childPidls.back())
                        return E_OUTOFMEMORY;
                }
            }

            return m_childPidls.empty() ? E_INVALIDARG : S_OK;
        }

        HRESULT EnsureUi()
        {
            if (m_ui)
                return S_OK;
            return EnsureManagedUi(m_folderPidl, m_ui);
        }

        HRESULT EnsureDescriptor(CComPtr<IXboxShellExtUi>& ui)
        {
            HRESULT hr = EnsureUi();
            if (FAILED(hr))
                return hr;
            ui = m_ui;
            if (m_hDescriptor)
                return S_OK;

            std::vector<LPCITEMIDLIST> pidlPtrs;
            pidlPtrs.reserve(m_childPidls.size());
            for (const auto& pidl : m_childPidls)
                pidlPtrs.push_back(pidl);

            HGLOBAL hGlobal = nullptr;
            hr = ui->GetDragFileGroupDescriptor(
                static_cast<UINT>(pidlPtrs.size()),
                pidlPtrs.data(),
                &hGlobal);
            if (FAILED(hr) || !hGlobal)
                return FAILED(hr) ? hr : E_FAIL;

            m_hDescriptor = CloneGlobal(hGlobal);
            GlobalFree(hGlobal);
            return m_hDescriptor ? S_OK : E_OUTOFMEMORY;
        }

        bool IsValidFileContentsIndex(LONG index) const
        {
            if (index < 0 || !m_hDescriptor)
                return false;

            const auto* group = static_cast<const FILEGROUPDESCRIPTORW*>(GlobalLock(m_hDescriptor));
            if (!group)
                return false;

            const bool valid = static_cast<UINT>(index) < group->cItems &&
                (group->fgd[index].dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
            GlobalUnlock(m_hDescriptor);
            return valid;
        }

        STDMETHOD(GetData)(FORMATETC* pFormatetc, STGMEDIUM* pMedium)
        {
            if (!pFormatetc || !pMedium)
                return E_POINTER;

            ZeroMemory(pMedium, sizeof(*pMedium));

            static const CLIPFORMAT cfPreferred =
                static_cast<CLIPFORMAT>(RegisterClipboardFormat(CFSTR_PREFERREDDROPEFFECT));
            static const CLIPFORMAT cfXbox =
                static_cast<CLIPFORMAT>(RegisterClipboardFormatA("XBOX_FILEDESCRIPTOR"));
            static const CLIPFORMAT cfFileDescW =
                static_cast<CLIPFORMAT>(RegisterClipboardFormatW(CFSTR_FILEDESCRIPTORW));
            static const CLIPFORMAT cfFileContents =
                static_cast<CLIPFORMAT>(RegisterClipboardFormat(CFSTR_FILECONTENTS));

            if (pFormatetc->cfFormat == cfPreferred)
            {
                HGLOBAL hGlobal = GlobalAlloc(GPTR, sizeof(DWORD));
                if (!hGlobal)
                    return E_OUTOFMEMORY;
                *static_cast<DWORD*>(GlobalLock(hGlobal)) = m_preferredEffect;
                GlobalUnlock(hGlobal);
                pMedium->tymed = TYMED_HGLOBAL;
                pMedium->hGlobal = hGlobal;
                return S_OK;
            }

            CComPtr<IXboxShellExtUi> ui;
            HRESULT hr = EnsureUi();
            if (FAILED(hr))
                return hr;
            ui = m_ui;

            if (pFormatetc->cfFormat == cfFileDescW || pFormatetc->cfFormat == cfXbox)
            {
                hr = EnsureDescriptor(ui);
                if (FAILED(hr))
                    return hr;

                HGLOBAL hGlobal = CloneGlobal(m_hDescriptor);
                if (!hGlobal)
                    return E_OUTOFMEMORY;

                pMedium->tymed = TYMED_HGLOBAL;
                pMedium->hGlobal = hGlobal;
                return S_OK;
            }

            if (pFormatetc->cfFormat == cfFileContents)
            {
                hr = EnsureDescriptor(ui);
                if (FAILED(hr))
                    return hr;

                if (!IsValidFileContentsIndex(pFormatetc->lindex))
                    return DV_E_LINDEX;

                CComPtr<IUnknown> stream;
                hr = ui->GetDragFileContentsStream(pFormatetc->lindex, &stream);
                if (FAILED(hr) || !stream)
                    return FAILED(hr) ? hr : DV_E_FORMATETC;

                CComPtr<IStream> fileStream;
                hr = stream->QueryInterface(IID_PPV_ARGS(&fileStream));
                if (FAILED(hr))
                    return DV_E_FORMATETC;

                pMedium->tymed = TYMED_ISTREAM;
                pMedium->pstm = fileStream.Detach();
                return S_OK;
            }

            return DV_E_FORMATETC;
        }

        STDMETHOD(GetDataHere)(FORMATETC*, STGMEDIUM*) { return DV_E_FORMATETC; }

        STDMETHOD(QueryGetData)(FORMATETC* pFormatetc)
        {
            if (!pFormatetc)
                return E_POINTER;

            static const CLIPFORMAT cfPreferred =
                static_cast<CLIPFORMAT>(RegisterClipboardFormat(CFSTR_PREFERREDDROPEFFECT));
            static const CLIPFORMAT cfXbox =
                static_cast<CLIPFORMAT>(RegisterClipboardFormatA("XBOX_FILEDESCRIPTOR"));
            static const CLIPFORMAT cfFileDescW =
                static_cast<CLIPFORMAT>(RegisterClipboardFormatW(CFSTR_FILEDESCRIPTORW));
            static const CLIPFORMAT cfFileContents =
                static_cast<CLIPFORMAT>(RegisterClipboardFormat(CFSTR_FILECONTENTS));

            if (pFormatetc->cfFormat == cfPreferred)
                return (pFormatetc->tymed & TYMED_HGLOBAL) ? S_OK : DV_E_TYMED;

            if (pFormatetc->cfFormat == cfFileDescW || pFormatetc->cfFormat == cfXbox)
                return (pFormatetc->tymed & TYMED_HGLOBAL) ? S_OK : DV_E_TYMED;

            if (pFormatetc->cfFormat == cfFileContents)
            {
                if (!(pFormatetc->tymed & TYMED_ISTREAM))
                    return DV_E_TYMED;
                if (pFormatetc->lindex < 0)
                    return DV_E_LINDEX;

                CComPtr<IXboxShellExtUi> ui;
                const HRESULT hr = EnsureDescriptor(ui);
                if (FAILED(hr))
                    return hr;

                return IsValidFileContentsIndex(pFormatetc->lindex) ? S_OK : DV_E_LINDEX;
            }

            return DV_E_FORMATETC;
        }

        STDMETHOD(GetCanonicalFormatEtc)(FORMATETC*, FORMATETC*) { return E_NOTIMPL; }

        STDMETHOD(SetData)(FORMATETC* pFormatetc, STGMEDIUM* pMedium, BOOL fRelease)
        {
            if (!pFormatetc || !pMedium)
                return E_POINTER;

            static const CLIPFORMAT cfPreferred =
                static_cast<CLIPFORMAT>(RegisterClipboardFormat(CFSTR_PREFERREDDROPEFFECT));
            if (pFormatetc->cfFormat != cfPreferred || pMedium->tymed != TYMED_HGLOBAL || !pMedium->hGlobal)
                return E_NOTIMPL;

            const DWORD* value = static_cast<const DWORD*>(GlobalLock(pMedium->hGlobal));
            if (value)
                m_preferredEffect = *value;
            GlobalUnlock(pMedium->hGlobal);

            if (fRelease)
                ReleaseStgMedium(pMedium);

            return S_OK;
        }

        STDMETHOD(EnumFormatEtc)(DWORD dwDirection, IEnumFORMATETC** ppenumFormatEtc)
        {
            if (!ppenumFormatEtc)
                return E_POINTER;

            *ppenumFormatEtc = nullptr;
            if (dwDirection != DATADIR_GET)
                return E_NOTIMPL;

            static const CLIPFORMAT cfPreferred =
                static_cast<CLIPFORMAT>(RegisterClipboardFormat(CFSTR_PREFERREDDROPEFFECT));
            static const CLIPFORMAT cfXbox =
                static_cast<CLIPFORMAT>(RegisterClipboardFormatA("XBOX_FILEDESCRIPTOR"));
            static const CLIPFORMAT cfFileDescW =
                static_cast<CLIPFORMAT>(RegisterClipboardFormatW(CFSTR_FILEDESCRIPTORW));
            static const CLIPFORMAT cfFileContents =
                static_cast<CLIPFORMAT>(RegisterClipboardFormat(CFSTR_FILECONTENTS));

            const FORMATETC formats[] = {
                { cfFileDescW, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL },
                { cfFileContents, nullptr, DVASPECT_CONTENT, -1, TYMED_ISTREAM },
                { cfXbox, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL },
                { cfPreferred, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL },
            };

            CComObject<CNativeEnumFormatEtc>* enumerator = nullptr;
            RH(CComObject<CNativeEnumFormatEtc>::CreateInstance(&enumerator));
            enumerator->Initialize(formats, ARRAYSIZE(formats));
            return enumerator->QueryInterface(IID_PPV_ARGS(ppenumFormatEtc));
        }

        STDMETHOD(DAdvise)(FORMATETC*, DWORD, IAdviseSink*, DWORD*) { return OLE_E_ADVISENOTSUPPORTED; }
        STDMETHOD(DUnadvise)(DWORD) { return OLE_E_ADVISENOTSUPPORTED; }
        STDMETHOD(EnumDAdvise)(IEnumSTATDATA**) { return OLE_E_ADVISENOTSUPPORTED; }

        STDMETHOD(SetAsyncMode)(BOOL) { return S_OK; }
        STDMETHOD(GetAsyncMode)(BOOL* pfIsOpAsync)
        {
            if (!pfIsOpAsync)
                return E_POINTER;
            // Let our TransferProgressForm own progress UI. Async mode makes Explorer
            // show its own "Copying..." dialog, which sits at 0% and hides our form.
            *pfIsOpAsync = FALSE;
            return S_OK;
        }
        STDMETHOD(StartOperation)(IBindCtx*)
        {
            m_asyncStarted = TRUE;
            return S_OK;
        }
        STDMETHOD(InOperation)(BOOL* pfInAsyncOp)
        {
            if (!pfInAsyncOp)
                return E_POINTER;
            *pfInAsyncOp = m_asyncStarted;
            return S_OK;
        }
        STDMETHOD(EndOperation)(HRESULT, IBindCtx*, DWORD)
        {
            m_asyncStarted = FALSE;
            return S_OK;
        }

        ~CNativeDataObject()
        {
            if (m_hDescriptor)
                GlobalFree(m_hDescriptor);
        }

    private:
        CComHeapPtr<ITEMIDLIST> m_folderPidl;
        std::vector<CComHeapPtr<ITEMIDLIST>> m_childPidls;
        CComPtr<IXboxShellExtUi> m_ui;
        HGLOBAL m_hDescriptor = nullptr;
        DWORD m_preferredEffect = DROPEFFECT_COPY;
        BOOL m_asyncStarted = FALSE;
    };
}

HRESULT CreateNativeDataObject(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    REFIID riid,
    void** ppv)
{
    XB_TRACE_SCOPE("CreateNativeDataObject");
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;
    if (riid != IID_IDataObject)
        return E_NOINTERFACE;

    if (cidl == 0 || !apidl)
        return E_INVALIDARG;

    CComObject<CNativeDataObject>* dataObject = nullptr;
    RH(CComObject<CNativeDataObject>::CreateInstance(&dataObject));
    RH(dataObject->Initialize(folderPidl, cidl, apidl));
    return dataObject->QueryInterface(riid, ppv);
}
