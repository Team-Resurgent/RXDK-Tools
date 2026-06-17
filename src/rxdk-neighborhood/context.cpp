#include "context.h"
#include "fileops.h"
#include "properties.h"
#include "wizard.h"
#include "utils.h"
#include <strsafe.h>

static void LoadMenuString(UINT id, char *buf, size_t cch)
{
    LoadStringA(g_app.hInstance, id, buf, (int)cch);
}

static void AppendMenuString(HMENU hMenu, UINT id, UINT cmd, UINT flags)
{
    char text[128];
    LoadMenuString(id, text, sizeof(text));
    AppendMenuA(hMenu, flags, cmd, text);
}

static INT_PTR CALLBACK RenameDlgProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_INITDIALOG:
        SetDlgItemTextA(hDlg, IDC_XB_TITLE, "Rename");
        SetWindowLongPtrA(hDlg, GWLP_USERDATA, lParam);
        SetDlgItemTextA(hDlg, IDC_XB_FILENAME, (LPCSTR)lParam);
        return TRUE;
    case WM_COMMAND:
        if (LOWORD(wParam) == IDOK)
        {
            GetDlgItemTextA(hDlg, IDC_XB_FILENAME, (LPSTR)GetWindowLongPtrA(hDlg, GWLP_USERDATA), MAX_PATH);
            EndDialog(hDlg, IDOK);
            return TRUE;
        }
        if (LOWORD(wParam) == IDCANCEL)
        {
            EndDialog(hDlg, IDCANCEL);
            return TRUE;
        }
        break;
    }
    return FALSE;
}

static BOOL PromptRename(const SelectionInfo *sel, char *newName, size_t cchNewName)
{
    if (sel->itemCount != 1)
        return FALSE;
    StringCchCopyA(newName, cchNewName, sel->items[0].name);
    return IDOK == DialogBoxParamA(g_app.hInstance, MAKEINTRESOURCEA(IDD_USERNAME_PROMPT), g_app.hwndMain, RenameDlgProc, (LPARAM)newName);
}

static void HandleListCommand(UINT cmd, SelectionInfo *sel)
{
    switch (cmd)
    {
    case IDM_CTX_OPEN:
        if ((sel->kind == SelDrive || sel->kind == SelFolder) && sel->itemCount == 1)
            AppOpenDisplayPath(sel->items[0].displayPath);
        else if (sel->itemCount == 1 && sel->items[0].hasAttrs && (sel->items[0].attrs.Attributes & FILE_ATTRIBUTE_DIRECTORY))
            SendMessageA(g_app.hwndMain, WM_COMMAND, IDM_NAV_OPEN, 0);
        break;
    case IDM_CTX_LAUNCH:
        FileOps_LaunchXbe(sel);
        break;
    case IDM_CTX_CUT:
        FileOps_Cut(sel);
        break;
    case IDM_CTX_COPY:
        FileOps_Copy(sel);
        break;
    case IDM_CTX_PASTE:
        FileOps_Paste(sel);
        break;
    case IDM_CTX_DELETE:
        FileOps_Delete(sel);
        break;
    case IDM_CTX_RENAME: {
        char newName[MAX_PATH];
        if (PromptRename(sel, newName, sizeof(newName)))
            FileOps_Rename(sel, newName);
        break;
    }
    case IDM_CTX_NEW_FOLDER:
        FileOps_NewFolder(sel);
        break;
    case IDM_CTX_EXPORT:
        FileOps_ExportToPc(sel);
        break;
    case IDM_CTX_PROPERTIES:
        Properties_Show(sel);
        break;
    case IDM_CTX_REBOOT_WARM:
        FileOps_Reboot(sel, FALSE, FALSE);
        break;
    case IDM_CTX_REBOOT_SAME:
        FileOps_Reboot(sel, FALSE, TRUE);
        break;
    case IDM_CTX_REBOOT_COLD:
        FileOps_Reboot(sel, TRUE, FALSE);
        break;
    case IDM_CTX_CAPTURE: {
        char filePath[MAX_PATH] = "screenshot.bmp";
        OPENFILENAMEA ofn = {0};
        IXboxConnection *conn = NULL;
        ofn.lStructSize = sizeof(ofn);
        ofn.hwndOwner = g_app.hwndMain;
        ofn.lpstrFilter = "Bitmap Files (*.bmp)\0*.bmp\0";
        ofn.lpstrFile = filePath;
        ofn.nMaxFile = MAX_PATH;
        ofn.Flags = OFN_OVERWRITEPROMPT;
        if (GetSaveFileNameA(&ofn) && SUCCEEDED(GetConsoleConnection(sel->consoleName, &conn)))
        {
            conn->HrScreenShot(filePath);
            conn->Release();
        }
        break;
    }
    default:
        break;
    }
}

void Context_ShowListMenu(int x, int y)
{
    SelectionInfo sel;
    HMENU hMenu;
    HMENU hNew;
    HMENU hReboot;
    UINT cmd;
    BOOL isXbe = FALSE;
    BOOL hasSelection;
    BOOL driveListing;
    BOOL driveLetterSelected;
    BOOL folderContext;

    hasSelection = AppBuildSelection(&sel);
    if (!hasSelection)
        hasSelection = AppFillFolderTarget(&sel);
    if (!hasSelection)
        return;

    driveListing = AppIsDriveListing();
    driveLetterSelected = driveListing && sel.kind == SelDrive && sel.itemCount > 0;
    folderContext = AppIsFolderView() && !driveLetterSelected;

    hMenu = CreatePopupMenu();
    hNew = CreatePopupMenu();
    hReboot = CreatePopupMenu();

    if (driveLetterSelected && sel.itemCount == 1)
    {
        AppendMenuString(hMenu, IDS_CM_OPEN, IDM_CTX_OPEN, MF_STRING);
        AppendMenuString(hMenu, IDS_CM_PROPERTIES, IDM_CTX_PROPERTIES, MF_STRING);
        cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, 0, g_app.hwndMain, NULL);
        DestroyMenu(hMenu);
        if (cmd)
            HandleListCommand(cmd, &sel);
        return;
    }

    if (sel.itemCount == 1 && sel.items[0].hasAttrs)
    {
        if (sel.items[0].attrs.Attributes & FILE_ATTRIBUTE_DIRECTORY)
            AppendMenuString(hMenu, IDS_CM_OPEN, IDM_CTX_OPEN, MF_STRING);
        else
        {
            LPCSTR ext = strrchr(sel.items[0].name, '.');
            if (ext && _stricmp(ext, ".xbe") == 0)
                isXbe = TRUE;
        }
    }

    if (isXbe)
        AppendMenuString(hMenu, IDS_CM_LAUNCH, IDM_CTX_LAUNCH, MF_STRING);

    AppendMenuString(hReboot, IDS_CM_REBOOT_WARM, IDM_CTX_REBOOT_WARM, MF_STRING);
    AppendMenuString(hReboot, IDS_CM_REBOOT_SAME_TITLE, IDM_CTX_REBOOT_SAME, MF_STRING);
    AppendMenuString(hReboot, IDS_CM_REBOOT_COLD, IDM_CTX_REBOOT_COLD, MF_STRING);

    if (folderContext && sel.itemCount == 0)
    {
        char rebootText[80];
        LoadMenuString(IDS_CM_REBOOT, rebootText, sizeof(rebootText));
        AppendMenuA(hMenu, MF_POPUP, (UINT_PTR)hReboot, rebootText);
        AppendMenuString(hMenu, IDS_CM_CAPTURE, IDM_CTX_CAPTURE, MF_STRING);
    }
    else if (sel.kind != SelFile && sel.kind != SelFiles)
    {
        char rebootText[80];
        LoadMenuString(IDS_CM_REBOOT, rebootText, sizeof(rebootText));
        AppendMenuA(hMenu, MF_POPUP, (UINT_PTR)hReboot, rebootText);
        AppendMenuString(hMenu, IDS_CM_CAPTURE, IDM_CTX_CAPTURE, MF_STRING);
    }

    if (folderContext || sel.kind == SelFile || sel.kind == SelFolder || sel.kind == SelFiles)
    {
        if (sel.itemCount > 0)
        {
            AppendMenuString(hMenu, IDS_CM_CUT, IDM_CTX_CUT, MF_STRING);
            AppendMenuString(hMenu, IDS_CM_COPY, IDM_CTX_COPY, MF_STRING);
            AppendMenuA(hMenu, MF_STRING, IDM_CTX_EXPORT, "Copy to &PC...");
        }
        if (FileOps_HasClipboard())
            AppendMenuString(hMenu, IDS_CM_PASTE, IDM_CTX_PASTE, MF_STRING);
        if (sel.itemCount > 0)
        {
            AppendMenuString(hMenu, IDS_CM_DELETE, IDM_CTX_DELETE, MF_STRING);
            if (sel.itemCount == 1)
                AppendMenuString(hMenu, IDS_CM_RENAME, IDM_CTX_RENAME, MF_STRING);
        }
        AppendMenuString(hNew, IDS_CM_NEW_FOLDER, IDM_CTX_NEW_FOLDER, MF_STRING);
        {
            char newText[80];
            LoadMenuString(IDS_CM_NEW, newText, sizeof(newText));
            AppendMenuA(hMenu, MF_POPUP, (UINT_PTR)hNew, newText);
        }
    }

    AppendMenuA(hMenu, MF_SEPARATOR, 0, NULL);
    AppendMenuString(hMenu, IDS_CM_PROPERTIES, IDM_CTX_PROPERTIES, MF_STRING);

    cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, 0, g_app.hwndMain, NULL);
    DestroyMenu(hMenu);

    if (cmd)
        HandleListCommand(cmd, &sel);
}

void Context_ShowTreeMenu(int x, int y)
{
    HTREEITEM hItem = TreeView_GetSelection(g_app.hwndTree);
    TVITEMA item = {0};
    NodeInfo *node;
    HMENU hMenu;
    HMENU hReboot;
    UINT cmd;
    SelectionInfo sel = {0};

    if (!hItem)
        return;

    item.mask = TVIF_PARAM;
    item.hItem = hItem;
    TreeView_GetItem(g_app.hwndTree, &item);
    node = (NodeInfo *)item.lParam;
    if (!node)
        return;

    hMenu = CreatePopupMenu();
    hReboot = CreatePopupMenu();

    switch (node->kind)
    {
    case NodeRoot:
        AppendMenuString(hMenu, IDS_CM_NEW_CONSOLE, IDM_CTX_NEW_CONSOLE, MF_STRING);
        break;
    case NodeConsole:
        StringCchCopyA(sel.consoleName, ARRAYSIZE(sel.consoleName), node->consoleName);
        sel.kind = SelConsole;
        sel.folderPath[0] = '\0';
        AppendMenuString(hReboot, IDS_CM_REBOOT_WARM, IDM_CTX_REBOOT_WARM, MF_STRING);
        AppendMenuString(hReboot, IDS_CM_REBOOT_SAME_TITLE, IDM_CTX_REBOOT_SAME, MF_STRING);
        AppendMenuString(hReboot, IDS_CM_REBOOT_COLD, IDM_CTX_REBOOT_COLD, MF_STRING);
        {
            char rebootText[80];
            LoadMenuString(IDS_CM_REBOOT, rebootText, sizeof(rebootText));
            AppendMenuA(hMenu, MF_POPUP, (UINT_PTR)hReboot, rebootText);
        }
        AppendMenuString(hMenu, IDS_CM_CAPTURE, IDM_CTX_CAPTURE, MF_STRING);
        AppendMenuString(hMenu, IDS_CM_SETDEFAULT, IDM_CTX_SET_DEFAULT, MF_STRING);
        AppendMenuString(hMenu, IDS_CM_SECURITY, IDM_CTX_SECURITY, MF_STRING);
        AppendMenuA(hMenu, MF_SEPARATOR, 0, NULL);
        AppendMenuString(hMenu, IDS_CM_PROPERTIES, IDM_CTX_PROPERTIES, MF_STRING);
        break;
    case NodeDrive:
        StringCchCopyA(sel.consoleName, ARRAYSIZE(sel.consoleName), node->consoleName);
        StringCchCopyA(sel.folderPath, ARRAYSIZE(sel.folderPath), node->displayPath);
        sel.kind = SelDrive;
        sel.itemCount = 1;
        StringCchCopyA(sel.items[0].displayPath, ARRAYSIZE(sel.items[0].displayPath), node->displayPath);
        BuildWirePath(node->displayPath, sel.items[0].wirePath, ARRAYSIZE(sel.items[0].wirePath));
        AppendMenuString(hMenu, IDS_CM_PROPERTIES, IDM_CTX_PROPERTIES, MF_STRING);
        break;
    case NodeFolder:
        StringCchCopyA(sel.consoleName, ARRAYSIZE(sel.consoleName), node->consoleName);
        StringCchCopyA(sel.folderPath, ARRAYSIZE(sel.folderPath), node->displayPath);
        sel.kind = SelFolder;
        sel.itemCount = 1;
        StringCchCopyA(sel.items[0].displayPath, ARRAYSIZE(sel.items[0].displayPath), node->displayPath);
        BuildWirePath(node->displayPath, sel.items[0].wirePath, ARRAYSIZE(sel.items[0].wirePath));
        {
            LPCSTR folderName = strrchr(node->displayPath, '\\');
            if (folderName && folderName[1])
                StringCchCopyA(sel.items[0].name, ARRAYSIZE(sel.items[0].name), folderName + 1);
        }
        AppendMenuString(hMenu, IDS_CM_OPEN, IDM_CTX_OPEN, MF_STRING);
        AppendMenuString(hMenu, IDS_CM_PROPERTIES, IDM_CTX_PROPERTIES, MF_STRING);
        break;
    default:
        break;
    }

    cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, 0, g_app.hwndMain, NULL);
    DestroyMenu(hMenu);

    switch (cmd)
    {
    case IDM_CTX_NEW_CONSOLE:
        ExecuteAddConsoleWizard();
        AppRefreshTree();
        break;
    case IDM_CTX_SET_DEFAULT:
        Consoles_SetDefault(node->consoleName);
        AppRefreshTree();
        break;
    case IDM_CTX_SECURITY:
    case IDM_CTX_PROPERTIES:
        Properties_Show(&sel);
        break;
    default:
        if (cmd)
            HandleListCommand(cmd, &sel);
        break;
    }
}
