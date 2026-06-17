#include "transfer.h"
#include "fileops.h"
#include "utils.h"
#include <objidl.h>
#include <shellapi.h>
#include <shlobj.h>
#include <strsafe.h>

struct CDropDataObject : public IDataObject
{
    LONG refCount;
    HGLOBAL hDrop;

    CDropDataObject(HGLOBAL drop) : refCount(1), hDrop(drop)
    {
    }

    STDMETHODIMP QueryInterface(REFIID riid, void **ppv) override
    {
        if (!ppv)
            return E_POINTER;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IDataObject))
        {
            *ppv = static_cast<IDataObject *>(this);
            AddRef();
            return S_OK;
        }
        *ppv = NULL;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount);
    }

    STDMETHODIMP_(ULONG) Release() override
    {
        ULONG count = InterlockedDecrement(&refCount);
        if (count == 0)
        {
            if (hDrop)
                GlobalFree(hDrop);
            delete this;
        }
        return count;
    }

    STDMETHODIMP GetData(FORMATETC *pFormatEtc, STGMEDIUM *pMedium) override
    {
        if (!pFormatEtc || !pMedium)
            return E_INVALIDARG;
        if (pFormatEtc->cfFormat != CF_HDROP || !(pFormatEtc->tymed & TYMED_HGLOBAL))
            return DV_E_FORMATETC;

        pMedium->tymed = TYMED_HGLOBAL;
        pMedium->hGlobal = hDrop;
        pMedium->pUnkForRelease = NULL;
        hDrop = NULL;
        return S_OK;
    }

    STDMETHODIMP GetDataHere(FORMATETC *, STGMEDIUM *) override
    {
        return E_NOTIMPL;
    }
    STDMETHODIMP QueryGetData(FORMATETC *pFormatEtc) override
    {
        if (!pFormatEtc)
            return E_INVALIDARG;
        if (pFormatEtc->cfFormat == CF_HDROP)
            return S_OK;
        return DV_E_FORMATETC;
    }
    STDMETHODIMP GetCanonicalFormatEtc(FORMATETC *, FORMATETC *) override
    {
        return E_NOTIMPL;
    }
    STDMETHODIMP SetData(FORMATETC *, STGMEDIUM *, BOOL) override
    {
        return E_NOTIMPL;
    }
    STDMETHODIMP EnumFormatEtc(DWORD, IEnumFORMATETC **) override
    {
        return E_NOTIMPL;
    }
    STDMETHODIMP DAdvise(FORMATETC *, DWORD, IAdviseSink *, DWORD *) override
    {
        return E_NOTIMPL;
    }
    STDMETHODIMP DUnadvise(DWORD) override
    {
        return E_NOTIMPL;
    }
    STDMETHODIMP EnumDAdvise(IEnumSTATDATA **) override
    {
        return E_NOTIMPL;
    }
};

struct CDropSource : public IDropSource
{
    LONG refCount;
    BOOL canceled;

    CDropSource() : refCount(1), canceled(FALSE)
    {
    }

    STDMETHODIMP QueryInterface(REFIID riid, void **ppv) override
    {
        if (!ppv)
            return E_POINTER;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IDropSource))
        {
            *ppv = static_cast<IDropSource *>(this);
            AddRef();
            return S_OK;
        }
        *ppv = NULL;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount);
    }

    STDMETHODIMP_(ULONG) Release() override
    {
        ULONG count = InterlockedDecrement(&refCount);
        if (count == 0)
            delete this;
        return count;
    }

    STDMETHODIMP QueryContinueDrag(BOOL fEscapePressed, DWORD) override
    {
        if (fEscapePressed)
        {
            canceled = TRUE;
            return DRAGDROP_S_CANCEL;
        }
        if (!(GetAsyncKeyState(VK_LBUTTON) & 0x8000))
            return canceled ? DRAGDROP_S_CANCEL : DRAGDROP_S_DROP;
        return S_OK;
    }

    STDMETHODIMP GiveFeedback(DWORD) override
    {
        return DRAGDROP_S_USEDEFAULTCURSORS;
    }
};

static HGLOBAL BuildDropFiles(const char *paths[], int count)
{
    size_t bytes = sizeof(DROPFILES);
    int i;

    for (i = 0; i < count; ++i)
        bytes += strlen(paths[i]) + 1;
    bytes += 1;

    HGLOBAL hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, bytes);
    DROPFILES *drop;
    char *dest;

    if (!hGlobal)
        return NULL;

    drop = (DROPFILES *)GlobalLock(hGlobal);
    drop->pFiles = sizeof(DROPFILES);
    drop->fWide = FALSE;
    dest = (char *)drop + sizeof(DROPFILES);
    for (i = 0; i < count; ++i)
    {
        StringCchCopyA(dest, bytes - (dest - (char *)drop), paths[i]);
        dest += strlen(dest) + 1;
    }
    GlobalUnlock(hGlobal);
    return hGlobal;
}

void Transfer_Init(HWND hwndList)
{
    DragAcceptFiles(hwndList, TRUE);
}

void Transfer_OnDropFiles(HDROP hDrop)
{
    char targetFolder[MAX_PATH * 2];
    char wireFolder[MAX_PATH];
    IXboxConnection *conn = NULL;
    UINT fileCount;
    UINT i;
    HRESULT hr = S_OK;

    if (g_app.currentConsole[0] == '\0' || g_app.currentPath[0] == '\0')
    {
        MessageBoxA(g_app.hwndMain, "Open a console folder before dropping files.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return;
    }

    if (_stricmp(g_app.currentPath, g_app.currentConsole) == 0)
    {
        MessageBoxA(g_app.hwndMain, "Open a drive or folder before dropping files.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return;
    }

    StringCchCopyA(targetFolder, ARRAYSIZE(targetFolder), g_app.currentPath);
    if (!BuildWirePath(targetFolder, wireFolder, ARRAYSIZE(wireFolder)))
        return;

    hr = GetConsoleConnection(g_app.currentConsole, &conn);
    if (FAILED(hr))
    {
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Could not connect to the Xbox.", hr);
        return;
    }

    fileCount = DragQueryFileA(hDrop, 0xFFFFFFFF, NULL, 0);
    for (i = 0; i < fileCount; ++i)
    {
        char localPath[MAX_PATH];
        char fileName[MAX_PATH];
        char targetWire[MAX_PATH];
        char *slash;

        DragQueryFileA(hDrop, i, localPath, MAX_PATH);
        slash = strrchr(localPath, '\\');
        StringCchCopyA(fileName, ARRAYSIZE(fileName), slash ? slash + 1 : localPath);
        StringCchPrintfA(targetWire, ARRAYSIZE(targetWire), "%s\\%s", wireFolder, fileName);

        hr = conn->HrSendFile(localPath, targetWire);
        if (FAILED(hr))
            break;
    }

    conn->Release();
    DragFinish(hDrop);

    if (SUCCEEDED(hr))
        AppRefreshCurrentView();
    else
        WindowUtils::MessageBoxResource(g_app.hwndMain, IDS_TRANSFER_FAILED, IDS_TRANSFER_FAILED_CAPTION, MB_OK | MB_ICONERROR);
}

void Transfer_OnListBeginDrag(LPNMLISTVIEW nm)
{
    SelectionInfo sel;
    IXboxConnection *conn = NULL;
    char tempRoot[MAX_PATH];
    char localPaths[MAX_SEL_ITEMS][MAX_PATH];
    const char *pathPtrs[MAX_SEL_ITEMS];
    HGLOBAL hDrop;
    CDropDataObject *dataObj;
    CDropSource *dropSource;
    DWORD effect;
    HRESULT hr;
    int i;
    int pathCount = 0;

    (void)nm;

    if (!AppBuildSelection(&sel) || sel.itemCount <= 0)
        return;

    if (AppIsDriveListing() && sel.kind == SelDrive)
        return;

    hr = GetConsoleConnection(sel.consoleName, &conn);
    if (FAILED(hr))
        return;

    if (GetTempPathA(MAX_PATH, tempRoot) == 0)
    {
        conn->Release();
        return;
    }
    StringCchCatA(tempRoot, ARRAYSIZE(tempRoot), "RXDKNeighborhoodDrag\\");
    SHCreateDirectoryExA(g_app.hwndMain, tempRoot, NULL);

    for (i = 0; i < sel.itemCount && pathCount < MAX_SEL_ITEMS; ++i)
    {
        const SelectedItem *item = &sel.items[i];
        BOOL isDir = item->hasAttrs && (item->attrs.Attributes & FILE_ATTRIBUTE_DIRECTORY);
        hr = FileOps_ReceiveWireToLocal(conn, item->wirePath, tempRoot, item->name, isDir);
        if (FAILED(hr))
            break;
        StringCchPrintfA(localPaths[pathCount], MAX_PATH, "%s%s", tempRoot, item->name);
        pathPtrs[pathCount] = localPaths[pathCount];
        pathCount++;
    }

    conn->Release();
    if (pathCount == 0 || FAILED(hr))
        return;

    hDrop = BuildDropFiles(pathPtrs, pathCount);
    if (!hDrop)
        return;

    dataObj = new CDropDataObject(hDrop);
    dropSource = new CDropSource();
    if (!dataObj || !dropSource)
    {
        if (dataObj)
            dataObj->Release();
        if (dropSource)
            dropSource->Release();
        GlobalFree(hDrop);
        return;
    }

    DoDragDrop(dataObj, dropSource, DROPEFFECT_COPY | DROPEFFECT_MOVE, &effect);
    dataObj->Release();
    dropSource->Release();
}
