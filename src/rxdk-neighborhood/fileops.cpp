#include "fileops.h"
#include "utils.h"
#include <shlobj.h>
#include <strsafe.h>

static struct
{
    FileClipOp op;
    char consoleName[80];
    char folderPath[MAX_PATH * 2];
    char names[MAX_SEL_ITEMS][256];
    int nameCount;
} g_clipboard;

static HRESULT DeleteWirePathRecursive(IXboxConnection *conn, LPCSTR wirePath, BOOL isDir)
{
    HRESULT hr;

    if (!isDir)
        return conn->HrDeleteFile(wirePath, FALSE);

    {
        PDM_WALK_DIR walkDir = NULL;
        char childPath[MAX_PATH];

        hr = conn->HrOpenDir(&walkDir, wirePath, NULL);
        if (FAILED(hr))
            return hr;

        for (;;)
        {
            DM_FILE_ATTRIBUTES fa;
            hr = conn->HrWalkDir(&walkDir, wirePath, &fa);
            if (FAILED(hr))
                break;

            StringCchPrintfA(childPath, ARRAYSIZE(childPath), "%s\\%s", wirePath, fa.Name);
            hr = DeleteWirePathRecursive(conn, childPath, (fa.Attributes & FILE_ATTRIBUTE_DIRECTORY) != 0);
            if (FAILED(hr))
                break;
        }

        conn->HrCloseDir(walkDir);
        if (FAILED(hr) && hr != XBDM_ENDOFLIST)
            return hr;
    }

    return conn->HrDeleteFile(wirePath, TRUE);
}

static BOOL ConfirmDeleteItems(HWND hwnd, const SelectionInfo *sel)
{
    char caption[128];
    char text[512];

    LoadStringA(g_app.hInstance, IDS_CONFIRM_DELETE_CAPTION, caption, sizeof(caption));
    if (sel->itemCount > 1)
    {
        WindowUtils::rsprintf(text, ARRAYSIZE(text), IDS_CONFIRM_DELETE_MULTIPLE, sel->itemCount);
    }
    else
    {
        const SelectedItem *item = &sel->items[0];
        BOOL isFolder = item->hasAttrs && (item->attrs.Attributes & FILE_ATTRIBUTE_DIRECTORY);
        UINT fmt = isFolder ? IDS_CONFIRM_DELETE_FOLDER : IDS_CONFIRM_DELETE;
        WindowUtils::rsprintf(text, ARRAYSIZE(text), fmt, item->name);
    }

    return IDYES == MessageBoxA(hwnd, text, caption, MB_YESNO | MB_ICONQUESTION);
}

void FileOps_ClearClipboard(void)
{
    g_clipboard.op = FileClipNone;
    g_clipboard.nameCount = 0;
}

BOOL FileOps_HasClipboard(void)
{
    return g_clipboard.op != FileClipNone && g_clipboard.nameCount > 0;
}

FileClipOp FileOps_GetClipboardOp(void)
{
    return g_clipboard.op;
}

void FileOps_Cut(const SelectionInfo *sel)
{
    int i;

    if (!sel || sel->itemCount <= 0)
        return;

    g_clipboard.op = FileClipCut;
    StringCchCopyA(g_clipboard.consoleName, ARRAYSIZE(g_clipboard.consoleName), sel->consoleName);
    StringCchCopyA(g_clipboard.folderPath, ARRAYSIZE(g_clipboard.folderPath), sel->folderPath);
    g_clipboard.nameCount = sel->itemCount;
    for (i = 0; i < sel->itemCount; ++i)
        StringCchCopyA(g_clipboard.names[i], ARRAYSIZE(g_clipboard.names[i]), sel->items[i].name);
}

void FileOps_Copy(const SelectionInfo *sel)
{
    int i;

    if (!sel || sel->itemCount <= 0)
        return;

    g_clipboard.op = FileClipCopy;
    StringCchCopyA(g_clipboard.consoleName, ARRAYSIZE(g_clipboard.consoleName), sel->consoleName);
    StringCchCopyA(g_clipboard.folderPath, ARRAYSIZE(g_clipboard.folderPath), sel->folderPath);
    g_clipboard.nameCount = sel->itemCount;
    for (i = 0; i < sel->itemCount; ++i)
        StringCchCopyA(g_clipboard.names[i], ARRAYSIZE(g_clipboard.names[i]), sel->items[i].name);
}

static HRESULT CopyWireItem(IXboxConnection *conn, LPCSTR srcWire, LPCSTR dstWire, BOOL isDir)
{
    HRESULT hr;
    char tempPath[MAX_PATH];

    if (!isDir)
    {
        if (GetTempPathA(MAX_PATH, tempPath) == 0)
            return HRESULT_FROM_WIN32(GetLastError());
        StringCchCatA(tempPath, ARRAYSIZE(tempPath), "rxdkxfer.tmp");

        hr = conn->HrReceiveFile(tempPath, srcWire);
        if (FAILED(hr))
            return hr;
        hr = conn->HrSendFile(tempPath, dstWire);
        DeleteFileA(tempPath);
        return hr;
    }

    hr = conn->HrMkdir(dstWire);
    if (FAILED(hr) && hr != XBDM_ALREADYEXISTS)
        return hr;

    {
        PDM_WALK_DIR walkDir = NULL;
        char childSrc[MAX_PATH];
        char childDst[MAX_PATH];

        hr = conn->HrOpenDir(&walkDir, srcWire, NULL);
        if (FAILED(hr))
            return hr;

        for (;;)
        {
            DM_FILE_ATTRIBUTES fa;
            hr = conn->HrWalkDir(&walkDir, srcWire, &fa);
            if (FAILED(hr))
                break;

            StringCchPrintfA(childSrc, ARRAYSIZE(childSrc), "%s\\%s", srcWire, fa.Name);
            StringCchPrintfA(childDst, ARRAYSIZE(childDst), "%s\\%s", dstWire, fa.Name);
            hr = CopyWireItem(conn, childSrc, childDst, (fa.Attributes & FILE_ATTRIBUTE_DIRECTORY) != 0);
            if (FAILED(hr))
                break;
        }

        conn->HrCloseDir(walkDir);
        if (FAILED(hr) && hr != XBDM_ENDOFLIST)
            return hr;
    }

    return S_OK;
}

HRESULT FileOps_Paste(const SelectionInfo *target)
{
    IXboxConnection *conn = NULL;
    HRESULT hr = S_OK;
    int i;
    char targetFolder[MAX_PATH * 2];

    if (!FileOps_HasClipboard())
    {
        WindowUtils::MessageBoxResource(g_app.hwndMain, IDS_NOTHING_TO_PASTE, IDS_ERROR_PASTE_CAPTION, MB_OK | MB_ICONINFORMATION);
        return S_FALSE;
    }

    if (!target || target->kind == SelRoot || target->kind == SelConsole || target->consoleName[0] == '\0')
        return E_INVALIDARG;

    if (AppIsDriveListing())
    {
        MessageBoxA(g_app.hwndMain, "Open a drive or folder before pasting.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return E_INVALIDARG;
    }

    if (_stricmp(target->consoleName, g_clipboard.consoleName) != 0)
    {
        MessageBoxA(g_app.hwndMain, "Cross-console paste is not supported yet.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return E_NOTIMPL;
    }

    if (target->kind == SelFolder && AppIsFolderView())
        StringCchCopyA(targetFolder, ARRAYSIZE(targetFolder), g_app.currentPath);
    else if (target->folderPath[0] != '\0' && _stricmp(target->folderPath, target->consoleName) != 0)
        StringCchCopyA(targetFolder, ARRAYSIZE(targetFolder), target->folderPath);
    else
        StringCchCopyA(targetFolder, ARRAYSIZE(targetFolder), g_app.currentPath);

    hr = GetConsoleConnection(target->consoleName, &conn);
    if (FAILED(hr))
        return hr;

    for (i = 0; i < g_clipboard.nameCount; ++i)
    {
        char srcWire[MAX_PATH];
        char dstWire[MAX_PATH];
        char dstDisplay[MAX_PATH * 2];
        DM_FILE_ATTRIBUTES fa;
        BOOL isDir = FALSE;

        if (!BuildWirePathInFolder(g_clipboard.folderPath, g_clipboard.names[i], srcWire, ARRAYSIZE(srcWire)))
            continue;
        if (!GetItemDisplayPath(targetFolder, g_clipboard.names[i], dstDisplay, ARRAYSIZE(dstDisplay)))
            continue;
        if (!BuildWirePath(dstDisplay, dstWire, ARRAYSIZE(dstWire)))
            continue;

        if (SUCCEEDED(conn->HrGetFileAttributes(srcWire, &fa)))
            isDir = (fa.Attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

        if (g_clipboard.op == FileClipCut && _stricmp(g_clipboard.folderPath, targetFolder) == 0)
        {
            hr = conn->HrRenameFile(srcWire, dstWire);
        }
        else
        {
            hr = CopyWireItem(conn, srcWire, dstWire, isDir);
            if (SUCCEEDED(hr) && g_clipboard.op == FileClipCut)
                DeleteWirePathRecursive(conn, srcWire, isDir);
        }

        if (FAILED(hr))
            break;
    }

    if (g_clipboard.op == FileClipCut && SUCCEEDED(hr))
        FileOps_ClearClipboard();

    conn->Release();
    if (SUCCEEDED(hr))
        AppRefreshCurrentView();
    else
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Paste failed.", hr);

    return hr;
}

HRESULT FileOps_Delete(const SelectionInfo *sel)
{
    IXboxConnection *conn = NULL;
    HRESULT hr;
    int i;

    if (!sel || sel->itemCount <= 0)
        return S_FALSE;

    if (AppIsDriveListing() && sel->kind == SelDrive)
        return E_INVALIDARG;

    if (!ConfirmDeleteItems(g_app.hwndMain, sel))
        return S_FALSE;

    hr = GetConsoleConnection(sel->consoleName, &conn);
    if (FAILED(hr))
        return hr;

    for (i = 0; i < sel->itemCount; ++i)
    {
        const SelectedItem *item = &sel->items[i];
        BOOL isDir = item->hasAttrs && (item->attrs.Attributes & FILE_ATTRIBUTE_DIRECTORY);
        hr = DeleteWirePathRecursive(conn, item->wirePath, isDir);
        if (FAILED(hr))
            break;
    }

    conn->Release();
    if (SUCCEEDED(hr))
        AppRefreshCurrentView();
    else
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Delete failed.", hr);

    return hr;
}

HRESULT FileOps_Rename(const SelectionInfo *sel, LPCSTR newName)
{
    IXboxConnection *conn = NULL;
    char newDisplay[MAX_PATH * 2];
    char newWire[MAX_PATH];
    HRESULT hr;

    if (!sel || sel->itemCount != 1 || !newName || !newName[0])
        return E_INVALIDARG;

    if (!GetItemDisplayPath(sel->folderPath, newName, newDisplay, ARRAYSIZE(newDisplay)))
        return E_INVALIDARG;
    if (!BuildWirePath(newDisplay, newWire, ARRAYSIZE(newWire)))
        return E_INVALIDARG;

    hr = GetConsoleConnection(sel->consoleName, &conn);
    if (FAILED(hr))
        return hr;

    hr = conn->HrRenameFile(sel->items[0].wirePath, newWire);
    conn->Release();

    if (SUCCEEDED(hr))
        AppRefreshCurrentView();
    else
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Rename failed.", hr);

    return hr;
}

HRESULT FileOps_NewFolder(const SelectionInfo *target)
{
    IXboxConnection *conn = NULL;
    char folderPath[MAX_PATH * 2];
    char wirePath[MAX_PATH];
    char name[64];
    int attempt;
    HRESULT hr;

    if (!target || target->consoleName[0] == '\0')
        return E_INVALIDARG;

    if (AppIsDriveListing())
    {
        MessageBoxA(g_app.hwndMain, "Open a drive or folder before creating a new folder.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return E_INVALIDARG;
    }

    if (!AppIsFolderView())
        return E_INVALIDARG;

    StringCchCopyA(folderPath, ARRAYSIZE(folderPath), g_app.currentPath);

    hr = GetConsoleConnection(target->consoleName, &conn);
    if (FAILED(hr))
        return hr;

    LoadStringA(g_app.hInstance, IDS_NEW_FOLDER, name, sizeof(name));
    for (attempt = 0; attempt < 100; ++attempt)
    {
        char tryName[64];
        char tryDisplay[MAX_PATH * 2];

        if (attempt == 0)
            StringCchCopyA(tryName, ARRAYSIZE(tryName), name);
        else
            StringCchPrintfA(tryName, ARRAYSIZE(tryName), "%s (%d)", name, attempt);

        if (!GetItemDisplayPath(folderPath, tryName, tryDisplay, ARRAYSIZE(tryDisplay)))
            continue;
        if (!BuildWirePath(tryDisplay, wirePath, ARRAYSIZE(wirePath)))
            continue;

        hr = conn->HrMkdir(wirePath);
        if (SUCCEEDED(hr) || hr == XBDM_ALREADYEXISTS)
        {
            if (hr == XBDM_ALREADYEXISTS)
                continue;
            break;
        }
        break;
    }

    conn->Release();
    if (SUCCEEDED(hr))
        AppRefreshCurrentView();
    else
        WindowUtils::MessageBoxResource(g_app.hwndMain, IDS_ERROR_CREATE_FOLDER, IDS_ERROR_CREATE_FOLDER_CAPTION, MB_OK | MB_ICONERROR);

    return hr;
}

HRESULT FileOps_LaunchXbe(const SelectionInfo *sel)
{
    IXboxConnection *conn = NULL;
    HRESULT hr;

    if (!sel || sel->itemCount != 1)
        return E_INVALIDARG;

    hr = GetConsoleConnection(sel->consoleName, &conn);
    if (FAILED(hr))
        return hr;

    hr = conn->HrReboot(DMBOOT_WARM, sel->items[0].wirePath);
    conn->Release();
    if (FAILED(hr))
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Launch failed.", hr);
    return hr;
}

static HRESULT ReceiveWireToLocalImpl(IXboxConnection *conn, LPCSTR wirePath, LPCSTR localDir, LPCSTR name, BOOL isDir)
{
    char localPath[MAX_PATH];
    HRESULT hr;

    StringCchPrintfA(localPath, ARRAYSIZE(localPath), "%s\\%s", localDir, name);
    if (!isDir)
        return conn->HrReceiveFile(localPath, wirePath);

    CreateDirectoryA(localPath, NULL);

    {
        PDM_WALK_DIR walkDir = NULL;
        char childWire[MAX_PATH];
        char childName[MAX_PATH];

        hr = conn->HrOpenDir(&walkDir, wirePath, NULL);
        if (FAILED(hr))
            return hr;

        for (;;)
        {
            DM_FILE_ATTRIBUTES fa;
            hr = conn->HrWalkDir(&walkDir, wirePath, &fa);
            if (FAILED(hr))
                break;

            StringCchCopyA(childName, ARRAYSIZE(childName), fa.Name);
            StringCchPrintfA(childWire, ARRAYSIZE(childWire), "%s\\%s", wirePath, fa.Name);
            hr = ReceiveWireToLocalImpl(conn, childWire, localPath, childName, (fa.Attributes & FILE_ATTRIBUTE_DIRECTORY) != 0);
            if (FAILED(hr))
                break;
        }

        conn->HrCloseDir(walkDir);
        if (FAILED(hr) && hr != XBDM_ENDOFLIST)
            return hr;
    }

    return S_OK;
}

HRESULT FileOps_ReceiveWireToLocal(IXboxConnection *conn, LPCSTR wirePath, LPCSTR localDir, LPCSTR name, BOOL isDir)
{
    return ReceiveWireToLocalImpl(conn, wirePath, localDir, name, isDir);
}

static int BrowseCallbackProc(HWND hwnd, UINT msg, LPARAM lp, LPARAM data)
{
    if (msg == BFFM_INITIALIZED)
        SendMessageA(hwnd, BFFM_SETSELECTIONA, TRUE, data);
    return 0;
}

HRESULT FileOps_ExportToPc(const SelectionInfo *sel)
{
    IXboxConnection *conn = NULL;
    BROWSEINFOA bi = {0};
    char displayPath[MAX_PATH] = "";
    LPITEMIDLIST pidl;
    HRESULT hr;
    int i;

    if (!sel || sel->itemCount <= 0 || sel->consoleName[0] == '\0')
        return E_INVALIDARG;

    if (AppIsDriveListing() && sel->kind == SelDrive)
        return E_INVALIDARG;

    hr = GetConsoleConnection(sel->consoleName, &conn);
    if (FAILED(hr))
        return hr;

    bi.hwndOwner = g_app.hwndMain;
    bi.lpszTitle = "Choose a folder on your PC to copy the selected items to:";
    bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
    bi.lpfn = BrowseCallbackProc;
    bi.lParam = (LPARAM)displayPath;
    pidl = SHBrowseForFolderA(&bi);
    if (!pidl)
    {
        conn->Release();
        return S_FALSE;
    }

    if (!SHGetPathFromIDListA(pidl, displayPath))
    {
        CoTaskMemFree(pidl);
        conn->Release();
        return E_FAIL;
    }
    CoTaskMemFree(pidl);

    for (i = 0; i < sel->itemCount; ++i)
    {
        const SelectedItem *item = &sel->items[i];
        BOOL isDir = item->hasAttrs && (item->attrs.Attributes & FILE_ATTRIBUTE_DIRECTORY);
        hr = ReceiveWireToLocalImpl(conn, item->wirePath, displayPath, item->name, isDir);
        if (FAILED(hr))
            break;
    }

    conn->Release();
    if (FAILED(hr))
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Export failed.", hr);
    else
        MessageBoxA(g_app.hwndMain, "Copy to PC completed.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);

    return hr;
}

HRESULT FileOps_Reboot(const SelectionInfo *sel, BOOL cold, BOOL sameTitle)
{
    IXboxConnection *conn = NULL;
    HRESULT hr;
    LPCSTR launchPath = NULL;
    DWORD flags = cold ? 0 : DMBOOT_WARM;

    if (!sel || sel->consoleName[0] == '\0')
        return E_INVALIDARG;

    if (sameTitle)
    {
        DM_XBE xbe;
        hr = GetConsoleConnection(sel->consoleName, &conn);
        if (FAILED(hr))
            return hr;
        hr = conn->HrGetXbeInfo(NULL, &xbe);
        if (SUCCEEDED(hr))
            launchPath = xbe.LaunchPath;
        conn->Release();
        conn = NULL;
        if (!launchPath || !launchPath[0])
            return E_FAIL;
    }
    else if (sel->itemCount == 1 && sel->items[0].wirePath[0])
    {
        launchPath = sel->items[0].wirePath;
    }

    hr = GetConsoleConnection(sel->consoleName, &conn);
    if (FAILED(hr))
        return hr;

    hr = conn->HrReboot(flags, launchPath);
    conn->Release();
    if (FAILED(hr))
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Reboot failed.", hr);
    return hr;
}
