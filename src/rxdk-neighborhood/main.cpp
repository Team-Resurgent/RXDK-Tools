#include "neighborhood.h"
#include "context.h"
#include "fileops.h"
#include "properties.h"
#include "transfer.h"
#include "wizard.h"
#include <stdio.h>
#include <stdlib.h>
#include <strsafe.h>

AppState g_app;

static void PopulateDriveList(LPCSTR consoleName);
static void PopulateDirectoryList(LPCSTR displayPath, LPCSTR consoleName);
static void RefreshTree(void);
static void RefreshCurrentView(void);

HINSTANCE AppInstance(void)
{
    return g_app.hInstance;
}

void AppRefreshTree(void)
{
    RefreshTree();
}

void AppRefreshCurrentView(void)
{
    RefreshCurrentView();
}

BOOL AppIsDriveListing(void)
{
    return g_app.currentConsole[0] != '\0' &&
           (g_app.currentPath[0] == '\0' || _stricmp(g_app.currentPath, g_app.currentConsole) == 0);
}

BOOL AppIsFolderView(void)
{
    return g_app.currentConsole[0] != '\0' && g_app.currentPath[0] != '\0' &&
           _stricmp(g_app.currentPath, g_app.currentConsole) != 0;
}

BOOL AppFillFolderTarget(SelectionInfo *sel)
{
    SelectedItem *item;
    LPCSTR folderName;
    IXboxConnection *conn = NULL;

    if (!sel || !AppIsFolderView())
        return FALSE;

    ZeroMemory(sel, sizeof(*sel));
    StringCchCopyA(sel->consoleName, ARRAYSIZE(sel->consoleName), g_app.currentConsole);
    StringCchCopyA(sel->folderPath, ARRAYSIZE(sel->folderPath), g_app.currentPath);
    sel->kind = SelFolder;
    sel->itemCount = 1;

    item = &sel->items[0];
    StringCchCopyA(item->displayPath, ARRAYSIZE(item->displayPath), g_app.currentPath);
    BuildWirePath(item->displayPath, item->wirePath, ARRAYSIZE(item->wirePath));

    folderName = strrchr(g_app.currentPath, '\\');
    if (folderName && folderName[1])
        StringCchCopyA(item->name, ARRAYSIZE(item->name), folderName + 1);
    else
        StringCchCopyA(item->name, ARRAYSIZE(item->name), g_app.currentPath);

    if (SUCCEEDED(GetConsoleConnection(g_app.currentConsole, &conn)))
    {
        if (SUCCEEDED(conn->HrGetFileAttributes(item->wirePath, &item->attrs)))
            item->hasAttrs = TRUE;
        conn->Release();
    }

    return TRUE;
}

static NodeInfo *AllocNodeInfo(NodeKind kind, LPCSTR displayPath, LPCSTR consoleName)
{
    NodeInfo *node = (NodeInfo *)calloc(1, sizeof(NodeInfo));
    if (!node)
        return NULL;

    node->kind = kind;
    if (displayPath)
        StringCchCopyA(node->displayPath, ARRAYSIZE(node->displayPath), displayPath);
    if (consoleName)
        StringCchCopyA(node->consoleName, ARRAYSIZE(node->consoleName), consoleName);
    return node;
}

static void FreeNodeInfo(NodeInfo *node)
{
    free(node);
}

static HTREEITEM InsertTreeItem(HTREEITEM hParent, LPCSTR text, NodeInfo *node)
{
    TVINSERTSTRUCTA ins = {0};
    ins.hParent = hParent;
    ins.hInsertAfter = TVI_LAST;
    ins.item.mask = TVIF_TEXT | TVIF_PARAM;
    ins.item.pszText = (LPSTR)text;
    ins.item.lParam = (LPARAM)node;
    return TreeView_InsertItem(g_app.hwndTree, &ins);
}

static NodeInfo *GetTreeNode(HTREEITEM hItem)
{
    TVITEMA item = {0};
    item.mask = TVIF_PARAM;
    item.hItem = hItem;
    if (!TreeView_GetItem(g_app.hwndTree, &item))
        return NULL;
    return (NodeInfo *)item.lParam;
}

static void SetStatusText(LPCSTR text)
{
    SendMessageA(g_app.hwndStatus, SB_SETTEXTA, 0, (LPARAM)text);
}

static void UpdateCurrentPath(LPCSTR displayPath, LPCSTR consoleName)
{
    if (displayPath)
        StringCchCopyA(g_app.currentPath, ARRAYSIZE(g_app.currentPath), displayPath);
    else
        g_app.currentPath[0] = '\0';

    if (consoleName)
        StringCchCopyA(g_app.currentConsole, ARRAYSIZE(g_app.currentConsole), consoleName);
    else
        g_app.currentConsole[0] = '\0';

    SetStatusText(displayPath && displayPath[0] ? displayPath : "Ready");
}

static void ClearList(void)
{
    ListView_DeleteAllItems(g_app.hwndList);
}

static void AddListItem(LPCSTR name, LPCSTR sizeText, LPCSTR typeText, LPCSTR dateText)
{
    LVITEMA item = {0};
    item.mask = LVIF_TEXT;
    item.iItem = ListView_GetItemCount(g_app.hwndList);
    item.pszText = (LPSTR)name;
    int index = ListView_InsertItem(g_app.hwndList, &item);
    if (index < 0)
        return;

    ListView_SetItemText(g_app.hwndList, index, 1, (LPSTR)sizeText);
    ListView_SetItemText(g_app.hwndList, index, 2, (LPSTR)typeText);
    ListView_SetItemText(g_app.hwndList, index, 3, (LPSTR)dateText);
}

static void PopulateDriveList(LPCSTR consoleName)
{
    IXboxConnection *conn = NULL;
    char drives[64];
    DWORD cDrives = sizeof(drives);
    HRESULT hr;

    ClearList();
    UpdateCurrentPath(consoleName, consoleName);

    hr = GetConsoleConnection(consoleName, &conn);
    if (FAILED(hr))
    {
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Could not connect to the Xbox.", hr);
        return;
    }

    hr = conn->HrGetDriveList(drives, &cDrives);
    conn->Release();
    if (FAILED(hr))
    {
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Could not read drive list.", hr);
        return;
    }

    for (DWORD i = 0; i < cDrives; ++i)
    {
        char label[8];
        StringCchPrintfA(label, ARRAYSIZE(label), "%c:", drives[i]);
        AddListItem(label, "", "Drive", "");
    }
}

static void PopulateDirectoryList(LPCSTR displayPath, LPCSTR consoleName)
{
    IXboxConnection *conn = NULL;
    PDM_WALK_DIR walkDir = NULL;
    char wirePath[MAX_PATH];
    HRESULT hr;

    ClearList();
    UpdateCurrentPath(displayPath, consoleName);

    if (!BuildWirePath(displayPath, wirePath, ARRAYSIZE(wirePath)))
        return;

    hr = GetConsoleConnection(consoleName, &conn);
    if (FAILED(hr))
    {
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Could not connect to the Xbox.", hr);
        return;
    }

    hr = conn->HrOpenDir(&walkDir, wirePath, NULL);
    if (FAILED(hr))
    {
        conn->Release();
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Could not open directory.", hr);
        return;
    }

    for (;;)
    {
        DM_FILE_ATTRIBUTES fa;
        char sizeText[64] = "";
        char dateText[64] = "";
        const char *typeText;

        hr = conn->HrWalkDir(&walkDir, wirePath, &fa);
        if (FAILED(hr))
            break;

        typeText = (fa.Attributes & FILE_ATTRIBUTE_DIRECTORY) ? "Folder" : "File";
        if (!(fa.Attributes & FILE_ATTRIBUTE_DIRECTORY))
            FormatFileSize(((ULONGLONG)fa.SizeHigh << 32) | fa.SizeLow, sizeText, ARRAYSIZE(sizeText));
        FormatFileTime(&fa.ChangeTime, dateText, ARRAYSIZE(dateText));
        AddListItem(fa.Name, sizeText, typeText, dateText);
    }

    conn->HrCloseDir(walkDir);
    conn->Release();
}

static void PopulateTreeDrives(HTREEITEM hConsoleItem, NodeInfo *consoleNode)
{
    IXboxConnection *conn = NULL;
    char drives[64];
    DWORD cDrives = sizeof(drives);
    HRESULT hr;

    HTREEITEM hChild = TreeView_GetChild(g_app.hwndTree, hConsoleItem);
    while (hChild)
    {
        HTREEITEM hNext = TreeView_GetNextSibling(g_app.hwndTree, hChild);
        TreeView_DeleteItem(g_app.hwndTree, hChild);
        hChild = hNext;
    }

    hr = GetConsoleConnection(consoleNode->consoleName, &conn);
    if (FAILED(hr))
        return;

    hr = conn->HrGetDriveList(drives, &cDrives);
    conn->Release();
    if (FAILED(hr))
        return;

    for (DWORD i = 0; i < cDrives; ++i)
    {
        NodeInfo *driveNode;
        char displayPath[MAX_PATH * 2];
        char label[8];

        StringCchPrintfA(displayPath, ARRAYSIZE(displayPath), "%s\\%c", consoleNode->consoleName, drives[i]);
        StringCchPrintfA(label, ARRAYSIZE(label), "%c:", drives[i]);
        driveNode = AllocNodeInfo(NodeDrive, displayPath, consoleNode->consoleName);
        InsertTreeItem(hConsoleItem, label, driveNode);
    }
}

struct EnumCtx
{
    HTREEITEM hRoot;
    char defaultName[80];
};

static BOOL AddConsoleToTree(LPCSTR name, void *ctx)
{
    EnumCtx *enumCtx = (EnumCtx *)ctx;
    NodeInfo *node;
    char label[96];

    node = AllocNodeInfo(NodeConsole, name, name);
    if (_stricmp(name, enumCtx->defaultName) == 0)
        StringCchPrintfA(label, ARRAYSIZE(label), "%s (default)", name);
    else
        StringCchCopyA(label, ARRAYSIZE(label), name);

    InsertTreeItem(enumCtx->hRoot, label, node);
    return TRUE;
}

static void RefreshTree(void)
{
    HTREEITEM hRoot;
    NodeInfo *rootNode;
    EnumCtx ctx;

    TreeView_DeleteAllItems(g_app.hwndTree);

    rootNode = AllocNodeInfo(NodeRoot, "Xbox Neighborhood", NULL);
    hRoot = InsertTreeItem(TVI_ROOT, "Xbox Neighborhood", rootNode);

    ctx.hRoot = hRoot;
    ctx.defaultName[0] = '\0';
    Consoles_GetDefault(ctx.defaultName, sizeof(ctx.defaultName));
    Consoles_Enumerate(AddConsoleToTree, &ctx);

    TreeView_Expand(g_app.hwndTree, hRoot, TVE_EXPAND);
    ClearList();
    UpdateCurrentPath(NULL, NULL);
}

static void RefreshCurrentView(void)
{
    if (g_app.currentConsole[0] == '\0')
    {
        RefreshTree();
        return;
    }

    if (g_app.currentPath[0] == '\0' || _stricmp(g_app.currentPath, g_app.currentConsole) == 0)
        PopulateDriveList(g_app.currentConsole);
    else
        PopulateDirectoryList(g_app.currentPath, g_app.currentConsole);
}

BOOL AppBuildSelection(SelectionInfo *sel)
{
    HTREEITEM hTreeItem;
    TVITEMA tvItem = {0};
    NodeInfo *treeNode = NULL;
    int index = -1;
    int count = 0;

    if (!sel)
        return FALSE;

    ZeroMemory(sel, sizeof(*sel));

    hTreeItem = TreeView_GetSelection(g_app.hwndTree);
    if (hTreeItem)
    {
        tvItem.mask = TVIF_PARAM;
        tvItem.hItem = hTreeItem;
        TreeView_GetItem(g_app.hwndTree, &tvItem);
        treeNode = (NodeInfo *)tvItem.lParam;
    }

    while ((index = ListView_GetNextItem(g_app.hwndList, index, LVNI_SELECTED)) >= 0)
    {
        SelectedItem *item;
        char name[256];
        LVITEMA lvItem = {0};
        IXboxConnection *conn = NULL;

        if (count >= MAX_SEL_ITEMS)
            break;

        lvItem.iItem = index;
        lvItem.pszText = name;
        lvItem.cchTextMax = sizeof(name);
        lvItem.mask = LVIF_TEXT;
        ListView_GetItem(g_app.hwndList, &lvItem);

        item = &sel->items[count];
        StringCchCopyA(item->name, ARRAYSIZE(item->name), name);
        count++;

        if (g_app.currentConsole[0] == '\0')
            continue;

        StringCchCopyA(sel->consoleName, ARRAYSIZE(sel->consoleName), g_app.currentConsole);

        if (g_app.currentPath[0] == '\0' || _stricmp(g_app.currentPath, g_app.currentConsole) == 0)
        {
            StringCchPrintfA(item->displayPath, ARRAYSIZE(item->displayPath), "%s\\%c", g_app.currentConsole, name[0]);
            BuildWirePath(item->displayPath, item->wirePath, ARRAYSIZE(item->wirePath));
            sel->kind = SelDrive;
            StringCchCopyA(sel->folderPath, ARRAYSIZE(sel->folderPath), g_app.currentConsole);
            continue;
        }

        StringCchCopyA(sel->folderPath, ARRAYSIZE(sel->folderPath), g_app.currentPath);
        GetItemDisplayPath(g_app.currentPath, name, item->displayPath, ARRAYSIZE(item->displayPath));
        BuildWirePath(item->displayPath, item->wirePath, ARRAYSIZE(item->wirePath));

        if (SUCCEEDED(GetConsoleConnection(g_app.currentConsole, &conn)))
        {
            if (SUCCEEDED(conn->HrGetFileAttributes(item->wirePath, &item->attrs)))
                item->hasAttrs = TRUE;
            else
            {
                PDM_WALK_DIR walkDir = NULL;
                if (SUCCEEDED(conn->HrOpenDir(&walkDir, item->wirePath, NULL)))
                {
                    conn->HrCloseDir(walkDir);
                    ZeroMemory(&item->attrs, sizeof(item->attrs));
                    item->attrs.Attributes = FILE_ATTRIBUTE_DIRECTORY;
                    item->hasAttrs = TRUE;
                }
            }
            conn->Release();
        }

        if (item->hasAttrs && (item->attrs.Attributes & FILE_ATTRIBUTE_DIRECTORY))
            sel->kind = (count > 1) ? SelFiles : SelFolder;
        else
            sel->kind = (count > 1) ? SelFiles : SelFile;
    }

    sel->itemCount = count;

    if (count == 0 && treeNode)
    {
        StringCchCopyA(sel->consoleName, ARRAYSIZE(sel->consoleName), treeNode->consoleName);
        switch (treeNode->kind)
        {
        case NodeConsole:
            sel->kind = SelConsole;
            StringCchCopyA(sel->folderPath, ARRAYSIZE(sel->folderPath), treeNode->consoleName);
            sel->itemCount = 1;
            StringCchCopyA(sel->items[0].name, ARRAYSIZE(sel->items[0].name), treeNode->consoleName);
            break;
        case NodeDrive:
            sel->kind = SelDrive;
            StringCchCopyA(sel->folderPath, ARRAYSIZE(sel->folderPath), treeNode->displayPath);
            sel->itemCount = 1;
            StringCchCopyA(sel->items[0].displayPath, ARRAYSIZE(sel->items[0].displayPath), treeNode->displayPath);
            BuildWirePath(treeNode->displayPath, sel->items[0].wirePath, ARRAYSIZE(sel->items[0].wirePath));
            break;
        case NodeFolder:
            sel->kind = SelFolder;
            StringCchCopyA(sel->folderPath, ARRAYSIZE(sel->folderPath), treeNode->displayPath);
            sel->itemCount = 1;
            StringCchCopyA(sel->items[0].displayPath, ARRAYSIZE(sel->items[0].displayPath), treeNode->displayPath);
            BuildWirePath(treeNode->displayPath, sel->items[0].wirePath, ARRAYSIZE(sel->items[0].wirePath));
            {
                LPCSTR folderName = strrchr(treeNode->displayPath, '\\');
                if (folderName && folderName[1])
                    StringCchCopyA(sel->items[0].name, ARRAYSIZE(sel->items[0].name), folderName + 1);
                else
                    StringCchCopyA(sel->items[0].name, ARRAYSIZE(sel->items[0].name), treeNode->displayPath);
            }
            break;
        default:
            break;
        }
    }

    return sel->kind != SelNone || sel->itemCount > 0;
}

void AppOpenDisplayPath(LPCSTR displayPath)
{
    if (!displayPath || !displayPath[0] || g_app.currentConsole[0] == '\0')
        return;
    PopulateDirectoryList(displayPath, g_app.currentConsole);
}

static void OnTreeSelect(LPNMTREEVIEWA nm)
{
    NodeInfo *node = GetTreeNode(nm->itemNew.hItem);
    if (!node)
        return;

    switch (node->kind)
    {
    case NodeRoot:
        ClearList();
        UpdateCurrentPath(NULL, NULL);
        break;
    case NodeConsole:
        PopulateDriveList(node->consoleName);
        PopulateTreeDrives(nm->itemNew.hItem, node);
        TreeView_Expand(g_app.hwndTree, nm->itemNew.hItem, TVE_EXPAND);
        break;
    case NodeDrive:
    case NodeFolder:
        PopulateDirectoryList(node->displayPath, node->consoleName);
        break;
    }
}

static void OnListDoubleClick(void)
{
    int index = ListView_GetNextItem(g_app.hwndList, -1, LVNI_SELECTED);
    char name[256];
    LVITEMA item = {0};

    if (index < 0 || g_app.currentConsole[0] == '\0')
        return;

    item.iItem = index;
    item.pszText = name;
    item.cchTextMax = sizeof(name);
    item.mask = LVIF_TEXT;
    ListView_GetItem(g_app.hwndList, &item);

    if (g_app.currentPath[0] == '\0' || _stricmp(g_app.currentPath, g_app.currentConsole) == 0)
    {
        char displayPath[MAX_PATH * 2];
        StringCchPrintfA(displayPath, ARRAYSIZE(displayPath), "%s\\%c", g_app.currentConsole, name[0]);
        PopulateDirectoryList(displayPath, g_app.currentConsole);
        return;
    }

    {
        char displayPath[MAX_PATH * 2];
        char wirePath[MAX_PATH];
        DM_FILE_ATTRIBUTES fa;
        IXboxConnection *conn = NULL;

        StringCchCopyA(displayPath, ARRAYSIZE(displayPath), g_app.currentPath);
        AppendDisplaySegment(displayPath, ARRAYSIZE(displayPath), name);

        if (!BuildWirePath(displayPath, wirePath, ARRAYSIZE(wirePath)))
            return;
        if (FAILED(GetConsoleConnection(g_app.currentConsole, &conn)))
            return;

        if (SUCCEEDED(conn->HrGetFileAttributes(wirePath, &fa)) && (fa.Attributes & FILE_ATTRIBUTE_DIRECTORY))
            PopulateDirectoryList(displayPath, g_app.currentConsole);

        conn->Release();
    }
}

static void NavigateUp(void)
{
    char parentPath[MAX_PATH * 2];

    if (g_app.currentPath[0] == '\0')
        return;

    if (_stricmp(g_app.currentPath, g_app.currentConsole) == 0)
    {
        PopulateDriveList(g_app.currentConsole);
        return;
    }

    if (!GetParentDisplayPath(g_app.currentPath, parentPath, ARRAYSIZE(parentPath)))
        return;

    if (_stricmp(parentPath, g_app.currentConsole) == 0)
        PopulateDriveList(g_app.currentConsole);
    else
        PopulateDirectoryList(parentPath, g_app.currentConsole);
}

static void AddConsole(void)
{
    ExecuteAddConsoleWizard();
}

static void RemoveSelectedConsole(void)
{
    HTREEITEM hItem = TreeView_GetSelection(g_app.hwndTree);
    NodeInfo *node = GetTreeNode(hItem);
    if (!node || node->kind != NodeConsole)
    {
        MessageBoxA(g_app.hwndMain, "Select a console to remove.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return;
    }

    if (!Consoles_Remove(node->consoleName))
    {
        MessageBoxA(
            g_app.hwndMain,
            "Could not remove the console. The default console cannot be removed.",
            "RXDKNeighborhood",
            MB_OK | MB_ICONERROR);
        return;
    }

    RefreshTree();
}

static void SetDefaultConsole(void)
{
    HTREEITEM hItem = TreeView_GetSelection(g_app.hwndTree);
    NodeInfo *node = GetTreeNode(hItem);
    if (!node || node->kind != NodeConsole)
    {
        MessageBoxA(g_app.hwndMain, "Select a console to make default.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return;
    }

    if (!Consoles_SetDefault(node->consoleName))
    {
        MessageBoxA(g_app.hwndMain, "Could not set the default console.", "RXDKNeighborhood", MB_OK | MB_ICONERROR);
        return;
    }

    RefreshTree();
}

static void RebootConsole(BOOL cold)
{
    IXboxConnection *conn = NULL;
    HRESULT hr;
    DWORD flags = cold ? 0 : DMBOOT_WARM;

    if (g_app.currentConsole[0] == '\0')
    {
        MessageBoxA(g_app.hwndMain, "Select a console first.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return;
    }

    hr = GetConsoleConnection(g_app.currentConsole, &conn);
    if (FAILED(hr))
    {
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Could not connect to the Xbox.", hr);
        return;
    }

    hr = conn->HrReboot(flags, NULL);
    conn->Release();
    if (FAILED(hr))
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Reboot failed.", hr);
}

static void CaptureScreenshot(void)
{
    char filePath[MAX_PATH] = "screenshot.bmp";
    OPENFILENAMEA ofn = {0};
    IXboxConnection *conn = NULL;
    HRESULT hr;

    if (g_app.currentConsole[0] == '\0')
    {
        MessageBoxA(g_app.hwndMain, "Select a console first.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
        return;
    }

    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = g_app.hwndMain;
    ofn.lpstrFilter = "Bitmap Files (*.bmp)\0*.bmp\0All Files (*.*)\0*.*\0";
    ofn.lpstrFile = filePath;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_OVERWRITEPROMPT;
    ofn.lpstrDefExt = "bmp";
    if (!GetSaveFileNameA(&ofn))
        return;

    hr = GetConsoleConnection(g_app.currentConsole, &conn);
    if (FAILED(hr))
    {
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Could not connect to the Xbox.", hr);
        return;
    }

    hr = conn->HrScreenShot(filePath);
    conn->Release();
    if (FAILED(hr))
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Screen capture failed.", hr);
    else
        MessageBoxA(g_app.hwndMain, "Screenshot saved.", "RXDKNeighborhood", MB_OK | MB_ICONINFORMATION);
}

static void LayoutControls(int width, int height)
{
    const int statusHeight = 22;
    int treeWidth = width / 4;
    if (treeWidth < 180)
        treeWidth = 180;

    MoveWindow(g_app.hwndTree, 0, 0, treeWidth, height - statusHeight, TRUE);
    MoveWindow(g_app.hwndList, treeWidth + 4, 0, width - treeWidth - 4, height - statusHeight, TRUE);
    MoveWindow(g_app.hwndStatus, 0, height - statusHeight, width, statusHeight, TRUE);
}

static void InitListColumns(void)
{
    LVCOLUMNA col = {0};
    col.mask = LVCF_TEXT | LVCF_WIDTH | LVCF_SUBITEM;
    col.pszText = (LPSTR) "Name";
    col.cx = 240;
    ListView_InsertColumn(g_app.hwndList, 0, &col);
    col.pszText = (LPSTR) "Size";
    col.cx = 100;
    ListView_InsertColumn(g_app.hwndList, 1, &col);
    col.pszText = (LPSTR) "Type";
    col.cx = 100;
    ListView_InsertColumn(g_app.hwndList, 2, &col);
    col.pszText = (LPSTR) "Modified";
    col.cx = 140;
    ListView_InsertColumn(g_app.hwndList, 3, &col);
}

static LRESULT CALLBACK MainWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE: {
        INITCOMMONCONTROLSEX icc = {sizeof(icc), ICC_TREEVIEW_CLASSES | ICC_LISTVIEW_CLASSES | ICC_BAR_CLASSES};
        InitCommonControlsEx(&icc);

        g_app.hwndTree = CreateWindowExA(
            WS_EX_CLIENTEDGE,
            WC_TREEVIEWA,
            "",
            WS_CHILD | WS_VISIBLE | TVS_HASLINES | TVS_HASBUTTONS | TVS_LINESATROOT | TVS_SHOWSELALWAYS,
            0,
            0,
            100,
            100,
            hwnd,
            (HMENU)IDC_TREE,
            g_app.hInstance,
            NULL);

        g_app.hwndList = CreateWindowExA(
            WS_EX_CLIENTEDGE,
            WC_LISTVIEWA,
            "",
            WS_CHILD | WS_VISIBLE | LVS_REPORT | LVS_SHOWSELALWAYS,
            0,
            0,
            100,
            100,
            hwnd,
            (HMENU)IDC_LIST,
            g_app.hInstance,
            NULL);

        g_app.hwndStatus = CreateWindowExA(
            0,
            STATUSCLASSNAMEA,
            "",
            WS_CHILD | WS_VISIBLE | SBARS_SIZEGRIP,
            0,
            0,
            100,
            20,
            hwnd,
            (HMENU)IDC_STATUS,
            g_app.hInstance,
            NULL);

        ListView_SetExtendedListViewStyle(g_app.hwndList, LVS_EX_FULLROWSELECT | LVS_EX_DOUBLEBUFFER);
        InitListColumns();
        Transfer_Init(g_app.hwndList);
        RefreshTree();
        return 0;
    }
    case WM_SIZE:
        LayoutControls(LOWORD(lParam), HIWORD(lParam));
        return 0;
    case WM_NOTIFY: {
        LPNMHDR hdr = (LPNMHDR)lParam;
        if (hdr->hwndFrom == g_app.hwndTree && hdr->code == TVN_SELCHANGEDA)
            OnTreeSelect((LPNMTREEVIEWA)lParam);
        if (hdr->hwndFrom == g_app.hwndTree && hdr->code == TVN_DELETEITEMA)
        {
            LPNMTREEVIEWA tv = (LPNMTREEVIEWA)lParam;
            FreeNodeInfo((NodeInfo *)tv->itemOld.lParam);
        }
        if (hdr->hwndFrom == g_app.hwndList && hdr->code == NM_DBLCLK)
            OnListDoubleClick();
        if (hdr->hwndFrom == g_app.hwndList && hdr->code == LVN_BEGINDRAG)
            Transfer_OnListBeginDrag((LPNMLISTVIEW)lParam);
        if (hdr->hwndFrom == g_app.hwndList && hdr->code == NM_RCLICK)
        {
            POINT pt;
            GetCursorPos(&pt);
            Context_ShowListMenu(pt.x, pt.y);
        }
        if (hdr->hwndFrom == g_app.hwndTree && hdr->code == NM_RCLICK)
        {
            POINT pt;
            GetCursorPos(&pt);
            Context_ShowTreeMenu(pt.x, pt.y);
        }
        return 0;
    }
    case WM_DROPFILES:
        Transfer_OnDropFiles((HDROP)wParam);
        return 0;
    case WM_KEYDOWN:
        if (wParam == VK_DELETE)
        {
            SelectionInfo sel;
            if (AppBuildSelection(&sel))
                FileOps_Delete(&sel);
            return 0;
        }
        if (wParam == VK_F2)
        {
            SelectionInfo sel;
            char newName[MAX_PATH];
            if (AppBuildSelection(&sel) && sel.itemCount == 1)
            {
                StringCchCopyA(newName, sizeof(newName), sel.items[0].name);
                if (IDOK == DialogBoxParamA(g_app.hInstance, MAKEINTRESOURCEA(IDD_USERNAME_PROMPT), g_app.hwndMain,
                                            [](HWND hDlg, UINT msg, WPARAM wp, LPARAM lp) -> INT_PTR {
                                                if (msg == WM_INITDIALOG)
                                                {
                                                    SetDlgItemTextA(hDlg, IDC_XB_TITLE, "Rename");
                                                    SetWindowLongPtrA(hDlg, GWLP_USERDATA, lp);
                                                    SetDlgItemTextA(hDlg, IDC_XB_FILENAME, (LPCSTR)lp);
                                                    return TRUE;
                                                }
                                                if (msg == WM_COMMAND && LOWORD(wp) == IDOK)
                                                {
                                                    GetDlgItemTextA(hDlg, IDC_XB_FILENAME, (LPSTR)GetWindowLongPtrA(hDlg, GWLP_USERDATA), MAX_PATH);
                                                    EndDialog(hDlg, IDOK);
                                                    return TRUE;
                                                }
                                                if (msg == WM_COMMAND && LOWORD(wp) == IDCANCEL)
                                                {
                                                    EndDialog(hDlg, IDCANCEL);
                                                    return TRUE;
                                                }
                                                return FALSE;
                                            },
                                            (LPARAM)newName))
                    FileOps_Rename(&sel, newName);
            }
            return 0;
        }
        break;
    case WM_COMMAND:
        switch (LOWORD(wParam))
        {
        case IDM_FILE_EXIT:
            DestroyWindow(hwnd);
            return 0;
        case IDM_CONSOLE_ADD:
            AddConsole();
            return 0;
        case IDM_CONSOLE_REMOVE:
            RemoveSelectedConsole();
            return 0;
        case IDM_CONSOLE_SET_DEFAULT:
            SetDefaultConsole();
            return 0;
        case IDM_CONSOLE_REFRESH:
            RefreshTree();
            return 0;
        case IDM_ACTION_REBOOT_WARM:
            RebootConsole(FALSE);
            return 0;
        case IDM_ACTION_REBOOT_COLD:
            RebootConsole(TRUE);
            return 0;
        case IDM_ACTION_SCREENSHOT:
            CaptureScreenshot();
            return 0;
        case IDM_NAV_UP:
            NavigateUp();
            return 0;
        case IDM_NAV_OPEN:
            OnListDoubleClick();
            return 0;
        case IDM_ACTION_REBOOT_SAME: {
            SelectionInfo sel;
            if (AppBuildSelection(&sel))
                FileOps_Reboot(&sel, FALSE, TRUE);
            return 0;
        }
        case IDM_CTX_OPEN:
        case IDM_CTX_LAUNCH:
        case IDM_CTX_CUT:
        case IDM_CTX_COPY:
        case IDM_CTX_PASTE:
        case IDM_CTX_DELETE:
        case IDM_CTX_RENAME:
        case IDM_CTX_NEW_FOLDER:
        case IDM_CTX_EXPORT:
        case IDM_CTX_PROPERTIES:
        case IDM_CTX_REBOOT_WARM:
        case IDM_CTX_REBOOT_SAME:
        case IDM_CTX_REBOOT_COLD:
        case IDM_CTX_CAPTURE:
        case IDM_CTX_NEW_CONSOLE:
        case IDM_CTX_SET_DEFAULT:
        case IDM_CTX_SECURITY: {
            SelectionInfo sel;
            if (!AppBuildSelection(&sel))
                AppFillFolderTarget(&sel);
            switch (LOWORD(wParam))
            {
            case IDM_CTX_OPEN:
                if (sel.kind == SelDrive && sel.itemCount == 1)
                    AppOpenDisplayPath(sel.items[0].displayPath);
                else
                    OnListDoubleClick();
                break;
            case IDM_CTX_LAUNCH:
                FileOps_LaunchXbe(&sel);
                break;
            case IDM_CTX_CUT:
                FileOps_Cut(&sel);
                break;
            case IDM_CTX_COPY:
                FileOps_Copy(&sel);
                break;
            case IDM_CTX_PASTE:
                FileOps_Paste(&sel);
                break;
            case IDM_CTX_DELETE:
                FileOps_Delete(&sel);
                break;
            case IDM_CTX_NEW_FOLDER:
                FileOps_NewFolder(&sel);
                break;
            case IDM_CTX_EXPORT:
                FileOps_ExportToPc(&sel);
                break;
            case IDM_CTX_PROPERTIES:
            case IDM_CTX_SECURITY:
                Properties_Show(&sel);
                break;
            case IDM_CTX_REBOOT_WARM:
                FileOps_Reboot(&sel, FALSE, FALSE);
                break;
            case IDM_CTX_REBOOT_SAME:
                FileOps_Reboot(&sel, FALSE, TRUE);
                break;
            case IDM_CTX_REBOOT_COLD:
                FileOps_Reboot(&sel, TRUE, FALSE);
                break;
            case IDM_CTX_NEW_CONSOLE:
                ExecuteAddConsoleWizard();
                AppRefreshTree();
                break;
            case IDM_CTX_SET_DEFAULT:
                if (sel.consoleName[0])
                {
                    Consoles_SetDefault(sel.consoleName);
                    AppRefreshTree();
                }
                break;
            default:
                break;
            }
            return 0;
        }
        }
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcA(hwnd, msg, wParam, lParam);
}

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE, LPSTR, int nCmdShow)
{
    WNDCLASSEXA wc = {0};
    MSG msg;

    g_app.hInstance = hInstance;
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    DmUseSharedConnection(TRUE);

    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = MainWndProc;
    wc.hInstance = hInstance;
    wc.hIcon = LoadIconA(hInstance, MAKEINTRESOURCEA(IDI_MAIN));
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.lpszClassName = "RXDKNeighborhoodClass";
    wc.lpszMenuName = MAKEINTRESOURCEA(IDR_MAINMENU);
    RegisterClassExA(&wc);

    g_app.hwndMain = CreateWindowExA(
        0,
        wc.lpszClassName,
        "Xbox Neighborhood",
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT,
        CW_USEDEFAULT,
        1100,
        700,
        NULL,
        NULL,
        hInstance,
        NULL);

    if (!g_app.hwndMain)
        return 1;

    ShowWindow(g_app.hwndMain, nCmdShow);
    UpdateWindow(g_app.hwndMain);

    while (GetMessage(&msg, NULL, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    DmUseSharedConnection(FALSE);
    CoUninitialize();
    return (int)msg.wParam;
}
