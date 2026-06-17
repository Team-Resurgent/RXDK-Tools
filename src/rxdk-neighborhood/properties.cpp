#include "properties.h"
#include "security.h"
#include "utils.h"
#include "manage.h"
#include "fileops.h"
#include "propdraw.h"
#include <strsafe.h>
#include <shellapi.h>

static PropCtx g_prop;

PropCtx *Properties_GetContext(void)
{
    return &g_prop;
}

static const COLORREF c_crPieColors[] =
    {
        RGB(0, 0, 255),
        RGB(255, 0, 255),
        RGB(0, 0, 128),
        RGB(128, 0, 128),
};

static UINT GetVolumeTypeResourceId(char driveLetter)
{
    driveLetter = (char)toupper((unsigned char)driveLetter);
    if (driveLetter >= 'F' && driveLetter <= 'M')
        return IDS_DRIVETYPE_MEMORY_UNIT;

    switch (driveLetter)
    {
    case 'C':
        return IDS_DRIVETYPE_MAIN_ROOT;
    case 'D':
        return IDS_DRIVETYPE_BOOT;
    case 'E':
        return IDS_DRIVETYPE_DEVELOPMENT;
    case 'S':
        return IDS_DRIVETYPE_TITLE_ROOT;
    case 'T':
        return IDS_DRIVETYPE_TITLE_CURRENT;
    case 'U':
        return IDS_DRIVETYPE_SAVED_CURRENT;
    case 'V':
        return IDS_DRIVETYPE_SAVED_ROOT;
    case 'X':
        return IDS_DRIVETYPE_SCRATCH;
    case 'Y':
        return IDS_DRIVETYPE_DASH;
    default:
        return IDS_DRIVETYPE_UNKNOWN;
    }
}

static BOOL GetDriveLetterFromSelection(const SelectionInfo *sel, char *letter)
{
    const SelectedItem *item;

    if (!sel || sel->itemCount < 1 || !letter)
        return FALSE;

    item = &sel->items[0];
    if (item->wirePath[0])
    {
        *letter = item->wirePath[0];
        return TRUE;
    }

    if (item->displayPath[0])
    {
        const char *slash = strrchr(item->displayPath, '\\');
        if (slash && slash[1])
        {
            *letter = slash[1];
            return TRUE;
        }
    }

    if (item->name[0])
    {
        *letter = item->name[0];
        return TRUE;
    }

    return FALSE;
}

static void BuildLocationString(LPCSTR displayPath, LPCSTR consoleName, LPSTR location, size_t cchLocation)
{
    char pathCopy[MAX_PATH * 2];
    char *slash;
    char *rest;

    location[0] = '\0';
    if (!displayPath || !consoleName || !displayPath[0])
        return;

    StringCchCopyA(pathCopy, ARRAYSIZE(pathCopy), displayPath);
    slash = strchr(pathCopy, '\\');
    if (!slash || !slash[1])
        return;

    rest = slash + 1;
    *slash = '\0';

    if (rest[1] == '\\' || rest[1] == '\0')
        StringCchPrintfA(location, cchLocation, "%c:%s (On '%s')", rest[0], rest + 1, consoleName);
    else
        StringCchPrintfA(location, cchLocation, "%c:%s (On '%s')", rest[0], rest + 1, consoleName);
}

static void BuildParentLocationString(LPCSTR itemDisplayPath, LPCSTR consoleName, LPSTR location, size_t cchLocation)
{
    char parentPath[MAX_PATH * 2];

    if (GetParentDisplayPath(itemDisplayPath, parentPath, ARRAYSIZE(parentPath)))
        BuildLocationString(parentPath, consoleName, location, cchLocation);
    else
        BuildLocationString(itemDisplayPath, consoleName, location, cchLocation);
}

static void GetFileTypeName(LPCSTR name, DWORD attributes, LPSTR typeName, size_t cchTypeName)
{
    SHFILEINFOA shellFileInfo;

    typeName[0] = '\0';
    if (!name)
        return;

    if (attributes & FILE_ATTRIBUTE_DIRECTORY)
    {
        LoadStringA(g_app.hInstance, IDS_PRELOAD_FOLDER_TYPE_NAME, typeName, (int)cchTypeName);
        return;
    }

    ZeroMemory(&shellFileInfo, sizeof(shellFileInfo));
    if (SHGetFileInfoA(name, attributes, &shellFileInfo, sizeof(shellFileInfo), SHGFI_USEFILEATTRIBUTES | SHGFI_TYPENAME))
        StringCchCopyA(typeName, cchTypeName, shellFileInfo.szTypeName);
}

static HRESULT CountFolderContentsRecursive(IXboxConnection *conn, LPCSTR wirePath, DWORD *fileCount, DWORD *folderCount, ULONGLONG *totalSize)
{
    PDM_WALK_DIR walkDir = NULL;
    HRESULT hr;
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

        if (fa.Attributes & FILE_ATTRIBUTE_DIRECTORY)
        {
            (*folderCount)++;
            StringCchPrintfA(childPath, ARRAYSIZE(childPath), "%s\\%s", wirePath, fa.Name);
            CountFolderContentsRecursive(conn, childPath, fileCount, folderCount, totalSize);
        }
        else
        {
            (*fileCount)++;
            *totalSize += ((ULONGLONG)fa.SizeHigh << 32) | fa.SizeLow;
        }
    }

    conn->HrCloseDir(walkDir);
    return S_OK;
}

static void MergeItemAttributes(FilePropInfo *info, const DM_FILE_ATTRIBUTES *attrs, LPCSTR name, BOOL getTypeName, BOOL *variousTypes)
{
    if (!info->prepared)
    {
        info->prepared = TRUE;
        info->creationTime = attrs->CreationTime;
        info->changeTime = attrs->ChangeTime;
        info->attributes = attrs->Attributes;
        info->validAttributes = FILE_ATTRIBUTE_READONLY | FILE_ATTRIBUTE_HIDDEN;
        if (getTypeName)
            GetFileTypeName(name, attrs->Attributes, info->typeName, ARRAYSIZE(info->typeName));
        return;
    }

    info->validAttributes &= ~(info->attributes ^ attrs->Attributes);
    info->attributes |= attrs->Attributes;

    if (getTypeName && !*variousTypes)
    {
        char typeName[MAX_PATH];
        GetFileTypeName(name, attrs->Attributes, typeName, ARRAYSIZE(typeName));
        if ((attrs->Attributes & FILE_ATTRIBUTE_DIRECTORY) != (info->attributes & FILE_ATTRIBUTE_DIRECTORY) ||
            (info->typeName[0] && typeName[0] && _stricmp(info->typeName, typeName) != 0))
        {
            *variousTypes = TRUE;
            LoadStringA(g_app.hInstance, IDS_FILETYPE_VARIOUS, info->typeName, ARRAYSIZE(info->typeName));
        }
    }
}

static BOOL Properties_PrepareSelection(PropCtx *ctx)
{
    SelectionInfo *sel = &ctx->sel;
    IXboxConnection *conn = ctx->conn;
    int i;

    ZeroMemory(&ctx->fileInfo, sizeof(ctx->fileInfo));
    ctx->pieShadowHeight = 0;

    if (sel->kind == SelDrive)
    {
        char letter = 0;
        char letterLabel[4];
        char driveWire[8];
        ULARGE_INTEGER freeSpace = {0};
        ULARGE_INTEGER totalSpace = {0};
        ULARGE_INTEGER bogus = {0};
        SelectedItem *item = &sel->items[0];

        if (!GetDriveLetterFromSelection(sel, &letter))
            return FALSE;

        if (!item->wirePath[0])
            BuildWirePath(item->displayPath, item->wirePath, ARRAYSIZE(item->wirePath));

        StringCchPrintfA(driveWire, ARRAYSIZE(driveWire), "%c:\\", letter);
        conn->HrGetDiskFreeSpace(driveWire, &freeSpace, &totalSpace, &bogus);

        ctx->totalSpace = totalSpace.QuadPart;
        ctx->freeSpace = freeSpace.QuadPart;
        ctx->driveTypeResourceId = GetVolumeTypeResourceId(letter);
        StringCchPrintfA(letterLabel, ARRAYSIZE(letterLabel), "%c:", letter);
        WindowUtils::rsprintf(ctx->driveDescription, ARRAYSIZE(ctx->driveDescription), IDS_NORMAL_NAME_FORMAT, letterLabel, sel->consoleName);
        return TRUE;
    }

    if (sel->kind != SelFile && sel->kind != SelFolder && sel->kind != SelFiles)
        return TRUE;

    for (i = 0; i < sel->itemCount; ++i)
    {
        SelectedItem *item = &sel->items[i];

        if (!item->wirePath[0])
            BuildWirePath(item->displayPath, item->wirePath, ARRAYSIZE(item->wirePath));

        if (!item->hasAttrs)
        {
            if (SUCCEEDED(conn->HrGetFileAttributes(item->wirePath, &item->attrs)))
                item->hasAttrs = TRUE;
        }
    }

    {
        BOOL variousTypes = FALSE;
        BOOL getTypeName = sel->itemCount < 2;

        for (i = 0; i < sel->itemCount; ++i)
        {
            const SelectedItem *item = &sel->items[i];
            ULONGLONG itemSize;

            if (!item->hasAttrs)
                continue;

            if (item->attrs.Attributes & FILE_ATTRIBUTE_DIRECTORY)
                ctx->fileInfo.folderCount++;
            else
                ctx->fileInfo.fileCount++;

            itemSize = ((ULONGLONG)item->attrs.SizeHigh << 32) | item->attrs.SizeLow;
            ctx->fileInfo.totalSize += itemSize;
            MergeItemAttributes(&ctx->fileInfo, &item->attrs, item->name, getTypeName, &variousTypes);
        }
    }

    if (sel->itemCount > 0)
        BuildParentLocationString(sel->items[0].displayPath, sel->consoleName, ctx->fileInfo.location, ARRAYSIZE(ctx->fileInfo.location));

    if (sel->kind == SelFolder && sel->itemCount == 1 && sel->items[0].wirePath[0])
    {
        DWORD files = 0;
        DWORD folders = 0;
        ULONGLONG size = 0;

        CountFolderContentsRecursive(conn, sel->items[0].wirePath, &files, &folders, &size);
        ctx->fileInfo.fileCount = files;
        ctx->fileInfo.folderCount = folders;
        ctx->fileInfo.totalSize = size;
        if (!ctx->fileInfo.typeName[0])
            LoadStringA(g_app.hInstance, IDS_PRELOAD_FOLDER_TYPE_NAME, ctx->fileInfo.typeName, ARRAYSIZE(ctx->fileInfo.typeName));
    }

    return TRUE;
}

static void DrawColorRect(HDC hdc, COLORREF crDraw, const RECT *prc)
{
    HBRUSH hbDraw = CreateSolidBrush(crDraw);
    if (hbDraw)
    {
        HBRUSH hbOld = (HBRUSH)SelectObject(hdc, hbDraw);
        PatBlt(hdc, prc->left, prc->top, prc->right - prc->left, prc->bottom - prc->top, PATCOPY);
        if (hbOld)
            SelectObject(hdc, hbOld);
        DeleteObject(hbDraw);
    }
}

static INT_PTR ConsoleGeneralDlgProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_INITDIALOG: {
        PropCtx *ctx = Properties_GetContext();
        DWORD dwIp = 0;
        DWORD dwSize;
        DM_XBE xbe;
        char buf[MAX_PATH];
        HRESULT hr;

        SetWindowLongPtrA(hDlg, DWLP_USER, (LONG_PTR)ctx);

        dwSize = sizeof(buf);
        if (SUCCEEDED(ctx->conn->HrGetNameOfXbox(buf, &dwSize, FALSE)))
            SetWindowTextA(GetDlgItem(hDlg, IDC_NAMEEDIT), buf);
        else
            SetWindowTextA(GetDlgItem(hDlg, IDC_NAMEEDIT), ctx->sel.consoleName);

        {
            CManageConsoles manageConsole;
            if (manageConsole.IsDefault(ctx->sel.consoleName))
            {
                HICON hDefaultConsole = LoadIconA(g_app.hInstance, MAKEINTRESOURCEA(IDI_CONSOLE_DEFAULT));
                if (hDefaultConsole)
                    WindowUtils::ReplaceWindowIcon(GetDlgItem(hDlg, IDC_ITEMICON), hDefaultConsole);
            }
        }

        if (SUCCEEDED(ctx->conn->HrResolveXboxName(&dwIp)))
            StringCchPrintfA(buf, ARRAYSIZE(buf), "%lu.%lu.%lu.%lu", (dwIp >> 24) & 0xFF, (dwIp >> 16) & 0xFF, (dwIp >> 8) & 0xFF, dwIp & 0xFF);
        else
            LoadStringA(g_app.hInstance, IDS_TITLE_NOT_AVAILABLE, buf, sizeof(buf));
        SetWindowTextA(GetDlgItem(hDlg, IDC_IPADDRESS), buf);

        if (SUCCEEDED(ctx->conn->HrGetAltAddress(&dwIp)))
        {
            StringCchPrintfA(buf, ARRAYSIZE(buf), "%lu.%lu.%lu.%lu", (dwIp >> 24) & 0xFF, (dwIp >> 16) & 0xFF, (dwIp >> 8) & 0xFF, dwIp & 0xFF);
            SetWindowTextA(GetDlgItem(hDlg, IDC_ALTIPADDRESS), buf);
        }
        else
        {
            ShowWindow(GetDlgItem(hDlg, IDC_ALTIPADDRESS), SW_HIDE);
            ShowWindow(GetDlgItem(hDlg, IDC_ALTIPADDRESS_TEXT), SW_HIDE);
        }

        hr = ctx->conn->HrGetXbeInfo(NULL, &xbe);
        if (hr == XBDM_NOSUCHFILE)
            LoadStringA(g_app.hInstance, IDS_DEFAULT_TITLE, xbe.LaunchPath, sizeof(xbe.LaunchPath));
        else if (FAILED(hr))
            LoadStringA(g_app.hInstance, IDS_TITLE_NOT_AVAILABLE, xbe.LaunchPath, sizeof(xbe.LaunchPath));

        {
            HWND hRunningTitle = GetDlgItem(hDlg, IDC_RUNNINGTITLE);
            SetWindowTextA(hRunningTitle, xbe.LaunchPath);
            SendMessageA(hRunningTitle, EM_SETSEL, (WPARAM)strlen(xbe.LaunchPath), (LPARAM)strlen(xbe.LaunchPath));
        }
        return TRUE;
    }
    }
    return FALSE;
}

static INT_PTR ConsoleAdvancedDlgProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_INITDIALOG:
        SetWindowLongPtrA(hDlg, DWLP_USER, (LONG_PTR)Properties_GetContext());
        CheckDlgButton(hDlg, IDC_WARMBOOT, BST_CHECKED);
        return TRUE;
    case WM_COMMAND:
        if (HIWORD(wParam) == BN_CLICKED)
        {
            PropCtx *ctx = (PropCtx *)GetWindowLongPtrA(hDlg, DWLP_USER);
            switch (LOWORD(wParam))
            {
            case IDC_REBOOT: {
                DM_XBE xbe;
                LPCSTR launchTitle = NULL;
                DWORD flags = 0;
                HRESULT hr;

                if (!ctx)
                    return TRUE;

                if (IsDlgButtonChecked(hDlg, IDC_WARMBOOT))
                    flags = DMBOOT_WARM;

                if (IsDlgButtonChecked(hDlg, IDC_RUNNINGTITLE) && SUCCEEDED(ctx->conn->HrGetXbeInfo(NULL, &xbe)))
                    launchTitle = xbe.LaunchPath;

                hr = ctx->conn->HrReboot(flags, launchTitle);
                if (FAILED(hr))
                    ShowHresultError(hDlg, "RXDKNeighborhood", "Could not reboot the Xbox.", hr);
                return TRUE;
            }
            case IDC_CAPTURE: {
                char filePath[MAX_PATH] = "screenshot.bmp";
                OPENFILENAMEA ofn = {0};
                HRESULT hr;

                ofn.lStructSize = sizeof(ofn);
                ofn.hwndOwner = hDlg;
                ofn.lpstrFilter = "Bitmap Files (*.bmp)\0*.bmp\0";
                ofn.lpstrFile = filePath;
                ofn.nMaxFile = MAX_PATH;
                ofn.Flags = OFN_OVERWRITEPROMPT;
                if (GetSaveFileNameA(&ofn))
                {
                    hr = g_prop.conn->HrScreenShot(filePath);
                    if (FAILED(hr))
                        ShowHresultError(hDlg, "RXDKNeighborhood", "Screen capture failed.", hr);
                }
                return TRUE;
            }
            }
        }
        break;
    }
    return FALSE;
}

static INT_PTR DriveGeneralDlgProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_INITDIALOG: {
        PropCtx *ctx = Properties_GetContext();
        char buf[128];
        ULONGLONG usedSpace;

        SetWindowLongPtrA(hDlg, DWLP_USER, (LONG_PTR)ctx);
        SetWindowTextA(GetDlgItem(hDlg, IDC_DRV_LETTER1), ctx->driveDescription);

        if (ctx->driveTypeResourceId != 0 &&
            LoadStringA(g_app.hInstance, ctx->driveTypeResourceId, buf, ARRAYSIZE(buf)))
            SetWindowTextA(GetDlgItem(hDlg, IDC_DRV_TYPE), buf);

        usedSpace = (ctx->totalSpace >= ctx->freeSpace) ? (ctx->totalSpace - ctx->freeSpace) : 0;

        FormatFileSizeBytes(usedSpace, buf, ARRAYSIZE(buf));
        SetWindowTextA(GetDlgItem(hDlg, IDC_DRV_USEDBYTES), buf);
        FormatFileSize(usedSpace, buf, ARRAYSIZE(buf));
        SetWindowTextA(GetDlgItem(hDlg, IDC_DRV_USEDMB), buf);

        FormatFileSizeBytes(ctx->freeSpace, buf, ARRAYSIZE(buf));
        SetWindowTextA(GetDlgItem(hDlg, IDC_DRV_FREEBYTES), buf);
        FormatFileSize(ctx->freeSpace, buf, ARRAYSIZE(buf));
        SetWindowTextA(GetDlgItem(hDlg, IDC_DRV_FREEMB), buf);

        FormatFileSizeBytes(ctx->totalSpace, buf, ARRAYSIZE(buf));
        SetWindowTextA(GetDlgItem(hDlg, IDC_DRV_TOTBYTES), buf);
        FormatFileSize(ctx->totalSpace, buf, ARRAYSIZE(buf));
        SetWindowTextA(GetDlgItem(hDlg, IDC_DRV_TOTMB), buf);
        return TRUE;
    }
    case WM_DRAWITEM: {
        LPDRAWITEMSTRUCT pDrawItem = (LPDRAWITEMSTRUCT)lParam;
        PropCtx *ctx = (PropCtx *)GetWindowLongPtrA(hDlg, DWLP_USER);

        if (!ctx)
            return FALSE;

        switch (pDrawItem->CtlID)
        {
        case IDC_DRV_PIE: {
            DWORD dwPctX10 = ctx->totalSpace ? (DWORD)((ULONGLONG)1000 * (ctx->totalSpace - ctx->freeSpace) / ctx->totalSpace) : 1000;

            if (!ctx->pieShadowHeight)
            {
                SIZE size;
                GetTextExtentPoint32A(pDrawItem->hDC, "W", 1, &size);
                ctx->pieShadowHeight = size.cy * 2 / 3;
            }

            DrawPie(
                pDrawItem->hDC,
                &pDrawItem->rcItem,
                dwPctX10,
                ctx->freeSpace == 0 || ctx->freeSpace == ctx->totalSpace,
                ctx->pieShadowHeight,
                c_crPieColors);
            return TRUE;
        }
        case IDC_DRV_USEDCOLOR:
            DrawColorRect(pDrawItem->hDC, c_crPieColors[DP_USEDCOLOR], &pDrawItem->rcItem);
            return TRUE;
        case IDC_DRV_FREECOLOR:
            DrawColorRect(pDrawItem->hDC, c_crPieColors[DP_FREECOLOR], &pDrawItem->rcItem);
            return TRUE;
        }
        break;
    }
    }
    (void)wParam;
    return FALSE;
}

static void SetReadOnlyHiddenState(HWND hDlg, const FilePropInfo *info)
{
    WPARAM readOnlyState = BST_INDETERMINATE;
    WPARAM hiddenState = BST_INDETERMINATE;

    if (info->validAttributes & FILE_ATTRIBUTE_READONLY)
        readOnlyState = (info->attributes & FILE_ATTRIBUTE_READONLY) ? BST_CHECKED : BST_UNCHECKED;
    if (info->validAttributes & FILE_ATTRIBUTE_HIDDEN)
        hiddenState = (info->attributes & FILE_ATTRIBUTE_HIDDEN) ? BST_CHECKED : BST_UNCHECKED;

    CheckDlgButton(hDlg, IDC_READONLY, readOnlyState);
    CheckDlgButton(hDlg, IDC_HIDDEN, hiddenState);
}

static INT_PTR FileGeneralDlgProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_INITDIALOG: {
        PropCtx *ctx = Properties_GetContext();
        const SelectedItem *item = NULL;
        const FilePropInfo *info = &ctx->fileInfo;
        char buf[128];
        HWND hIcon;

        SetWindowLongPtrA(hDlg, DWLP_USER, (LONG_PTR)ctx);

        if (ctx->sel.itemCount > 0)
            item = &ctx->sel.items[0];

        if (ctx->sel.itemCount == 1 && item)
            SetWindowTextA(GetDlgItem(hDlg, IDC_NAMEEDIT), item->name);
        else
            SetWindowTextA(GetDlgItem(hDlg, IDC_NAMEEDIT), "");

        hIcon = GetDlgItem(hDlg, IDC_ITEMICON);
        if (hIcon && ctx->sel.itemCount == 1 && item)
        {
            SHFILEINFOA shellFileInfo;
            if (SHGetFileInfoA(item->name, item->hasAttrs ? item->attrs.Attributes : FILE_ATTRIBUTE_DIRECTORY, &shellFileInfo, sizeof(shellFileInfo), SHGFI_USEFILEATTRIBUTES | SHGFI_ICON | SHGFI_LARGEICON))
                WindowUtils::ReplaceWindowIcon(hIcon, shellFileInfo.hIcon);
        }

        if (info->typeName[0])
            SetWindowTextA(GetDlgItem(hDlg, IDC_FILETYPE), info->typeName);

        if (info->location[0])
            SetWindowTextA(GetDlgItem(hDlg, IDC_LOCATION), info->location);

        if (info->totalSize || ctx->sel.kind == SelFile || ctx->sel.kind == SelFiles)
        {
            FormatFileSize(info->totalSize, buf, ARRAYSIZE(buf));
            SetWindowTextA(GetDlgItem(hDlg, IDC_FILESIZE), buf);
        }

        if (ctx->sel.kind == SelFolder || ctx->sel.kind == SelFiles)
        {
            WindowUtils::rsprintf(buf, ARRAYSIZE(buf), IDS_CONTAINS_FORMAT, info->fileCount, info->folderCount);
            SetWindowTextA(GetDlgItem(hDlg, IDC_CONTAINS), buf);
        }

        if (info->prepared)
        {
            FormatFileTime(&info->creationTime, buf, ARRAYSIZE(buf));
            SetWindowTextA(GetDlgItem(hDlg, IDC_CREATED), buf);
            FormatFileTime(&info->changeTime, buf, ARRAYSIZE(buf));
            SetWindowTextA(GetDlgItem(hDlg, IDC_LASTMODIFIED), buf);
            SetReadOnlyHiddenState(hDlg, info);
        }
        else if (item && item->hasAttrs)
        {
            FormatFileTime(&item->attrs.CreationTime, buf, ARRAYSIZE(buf));
            SetWindowTextA(GetDlgItem(hDlg, IDC_CREATED), buf);
            FormatFileTime(&item->attrs.ChangeTime, buf, ARRAYSIZE(buf));
            SetWindowTextA(GetDlgItem(hDlg, IDC_LASTMODIFIED), buf);
            CheckDlgButton(hDlg, IDC_READONLY, (item->attrs.Attributes & FILE_ATTRIBUTE_READONLY) ? BST_CHECKED : BST_UNCHECKED);
            CheckDlgButton(hDlg, IDC_HIDDEN, (item->attrs.Attributes & FILE_ATTRIBUTE_HIDDEN) ? BST_CHECKED : BST_UNCHECKED);
        }
        return TRUE;
    }
    case WM_NOTIFY: {
        LPNMHDR hdr = (LPNMHDR)lParam;
        if (hdr->code == PSN_APPLY)
        {
            PropCtx *ctx = (PropCtx *)GetWindowLongPtrA(hDlg, DWLP_USER);
            DM_FILE_ATTRIBUTES fa;
            DWORD attrs;
            int i;

            if (!ctx || ctx->sel.itemCount != 1 || !ctx->sel.items[0].hasAttrs)
            {
                SetWindowLongPtrA(hDlg, DWLP_MSGRESULT, PSNRET_NOERROR);
                return TRUE;
            }

            fa = ctx->sel.items[0].attrs;
            attrs = fa.Attributes & ~(FILE_ATTRIBUTE_READONLY | FILE_ATTRIBUTE_HIDDEN);
            if (IsDlgButtonChecked(hDlg, IDC_READONLY) == BST_CHECKED)
                attrs |= FILE_ATTRIBUTE_READONLY;
            if (IsDlgButtonChecked(hDlg, IDC_HIDDEN) == BST_CHECKED)
                attrs |= FILE_ATTRIBUTE_HIDDEN;

            if (attrs != fa.Attributes)
            {
                fa.Attributes = attrs;
                ctx->conn->HrSetFileAttributes(ctx->sel.items[0].wirePath, &fa);
                ctx->sel.items[0].attrs = fa;
                AppRefreshCurrentView();
            }

            for (i = 1; i < ctx->sel.itemCount; ++i)
            {
                if (!ctx->sel.items[i].hasAttrs)
                    continue;

                fa = ctx->sel.items[i].attrs;
                attrs = fa.Attributes & ~(FILE_ATTRIBUTE_READONLY | FILE_ATTRIBUTE_HIDDEN);
                if (IsDlgButtonChecked(hDlg, IDC_READONLY) == BST_CHECKED)
                    attrs |= FILE_ATTRIBUTE_READONLY;
                if (IsDlgButtonChecked(hDlg, IDC_HIDDEN) == BST_CHECKED)
                    attrs |= FILE_ATTRIBUTE_HIDDEN;

                if (attrs != fa.Attributes)
                {
                    fa.Attributes = attrs;
                    ctx->conn->HrSetFileAttributes(ctx->sel.items[i].wirePath, &fa);
                }
            }

            if (ctx->sel.itemCount > 1)
                AppRefreshCurrentView();

            SetWindowLongPtrA(hDlg, DWLP_MSGRESULT, PSNRET_NOERROR);
            return TRUE;
        }
        break;
    }
    }
    (void)wParam;
    return FALSE;
}

void Properties_Show(const SelectionInfo *sel)
{
    PROPSHEETPAGEA pages[4];
    PROPSHEETHEADERA header;
    int pageCount = 0;
    HRESULT hr;
    LPCSTR caption;

    if (!sel || sel->kind == SelNone || sel->kind == SelRoot)
        return;

    if ((sel->kind == SelFile || sel->kind == SelFolder || sel->kind == SelFiles) && sel->itemCount == 0)
        return;

    ZeroMemory(&g_prop, sizeof(g_prop));
    g_prop.sel = *sel;

    hr = GetConsoleConnection(sel->consoleName, &g_prop.conn);
    if (FAILED(hr))
    {
        ShowHresultError(g_app.hwndMain, "RXDKNeighborhood", "Could not connect to the Xbox.", hr);
        return;
    }

    if (!Properties_PrepareSelection(&g_prop))
    {
        g_prop.conn->Release();
        g_prop.conn = NULL;
        return;
    }

    ZeroMemory(pages, sizeof(pages));

    if (sel->kind == SelConsole)
    {
        pages[pageCount].dwSize = sizeof(PROPSHEETPAGEA);
        pages[pageCount].dwFlags = PSP_DEFAULT | PSP_USETITLE;
        pages[pageCount].hInstance = g_app.hInstance;
        pages[pageCount].pszTemplate = MAKEINTRESOURCEA(IDD_CONSOLE_GENERAL);
        pages[pageCount].pfnDlgProc = ConsoleGeneralDlgProc;
        pages[pageCount].pszTitle = "General";
        pages[pageCount].lParam = (LPARAM)&g_prop;
        pageCount++;

        pages[pageCount].dwSize = sizeof(PROPSHEETPAGEA);
        pages[pageCount].dwFlags = PSP_DEFAULT | PSP_USETITLE;
        pages[pageCount].hInstance = g_app.hInstance;
        pages[pageCount].pszTemplate = MAKEINTRESOURCEA(IDD_CONSOLE_ADVANCED);
        pages[pageCount].pfnDlgProc = ConsoleAdvancedDlgProc;
        pages[pageCount].pszTitle = "Advanced";
        pages[pageCount].lParam = (LPARAM)&g_prop;
        pageCount++;

        pages[pageCount].dwSize = sizeof(PROPSHEETPAGEA);
        pages[pageCount].dwFlags = PSP_DEFAULT | PSP_USETITLE;
        pages[pageCount].hInstance = g_app.hInstance;
        pages[pageCount].pszTemplate = MAKEINTRESOURCEA(IDD_CONSOLE_SECURITY);
        pages[pageCount].pfnDlgProc = SecurityPage_DialogProc;
        pages[pageCount].pszTitle = "Security";
        pages[pageCount].lParam = (LPARAM)&g_prop;
        pageCount++;
    }
    else if (sel->kind == SelDrive)
    {
        pages[pageCount].dwSize = sizeof(PROPSHEETPAGEA);
        pages[pageCount].dwFlags = PSP_DEFAULT | PSP_USETITLE;
        pages[pageCount].hInstance = g_app.hInstance;
        pages[pageCount].pszTemplate = MAKEINTRESOURCEA(IDD_DRV_GENERAL);
        pages[pageCount].pfnDlgProc = DriveGeneralDlgProc;
        pages[pageCount].pszTitle = g_prop.driveDescription;
        pages[pageCount].lParam = (LPARAM)&g_prop;
        pageCount++;
    }
    else if (sel->kind == SelFile || sel->kind == SelFolder || sel->kind == SelFiles)
    {
        UINT templateId = (sel->itemCount > 1) ? IDD_FILEMULTPROP : ((sel->kind == SelFolder) ? IDD_FOLDERPROP : IDD_FILEPROP);
        pages[pageCount].dwSize = sizeof(PROPSHEETPAGEA);
        pages[pageCount].dwFlags = PSP_DEFAULT | PSP_USETITLE;
        pages[pageCount].hInstance = g_app.hInstance;
        pages[pageCount].pszTemplate = MAKEINTRESOURCEA(templateId);
        pages[pageCount].pfnDlgProc = FileGeneralDlgProc;
        pages[pageCount].pszTitle = "General";
        pages[pageCount].lParam = (LPARAM)&g_prop;
        pageCount++;
    }

    if (pageCount == 0)
    {
        g_prop.conn->Release();
        return;
    }

    caption = sel->consoleName;
    if (sel->kind == SelDrive && g_prop.driveDescription[0])
        caption = g_prop.driveDescription;

    ZeroMemory(&header, sizeof(header));
    header.dwSize = sizeof(header);
    header.dwFlags = PSH_PROPSHEETPAGE;
    if (sel->kind == SelConsole || sel->kind == SelDrive)
        header.dwFlags |= PSH_NOAPPLYNOW;
    header.hwndParent = g_app.hwndMain;
    header.hInstance = g_app.hInstance;
    header.pszCaption = caption[0] ? caption : "Properties";
    header.nPages = pageCount;
    header.ppsp = pages;

    PropertySheetA(&header);
    g_prop.conn->Release();
    g_prop.conn = NULL;
}
