#include "security.h"
#include "properties.h"
#include "utils.h"
#include "fileops.h"
#include <strsafe.h>

#define UACF_ADD 0x01
#define UACF_REMOVE 0x02
#define UACF_ADDANDREMOVE (UACF_ADD | UACF_REMOVE)

struct UserAccessChange
{
    UserAccessChange *next;
    DM_USER dmUser;
    DWORD dwNewAccess;
    DWORD dwFlags;
};

struct SecurityState
{
    PropCtx *ctx;
    IXboxConnection *conn;
    char consoleName[80];
    HWND hDlg;
    HWND hWndAccess;
    HWND hWndUsers;
    BOOL fLocked;
    BOOL fManageMode;
    BOOL fSecureMode;
    DWORD dwAccess;
    UserAccessChange *userList;
    int iLastSelected;
    BOOL fUpdatingUI;
};

#ifndef DMPL_PRIV_ALL
#define DMPL_PRIV_ALL (DMPL_PRIV_READ | DMPL_PRIV_WRITE | DMPL_PRIV_MANAGE | DMPL_PRIV_CONFIGURE | DMPL_PRIV_CONTROL)
#endif
#ifndef DMPL_PRIV_INITIAL
#define DMPL_PRIV_INITIAL (DMPL_PRIV_READ | DMPL_PRIV_WRITE)
#endif

static DWORD AccessBitFromResource(int resourceId)
{
    switch (resourceId)
    {
    case IDS_PERMISSION_READ:
        return DMPL_PRIV_READ;
    case IDS_PERMISSION_WRITE:
        return DMPL_PRIV_WRITE;
    case IDS_PERMISSION_MANAGE:
        return DMPL_PRIV_MANAGE;
    case IDS_PERMISSION_CONFIGURE:
        return DMPL_PRIV_CONFIGURE;
    case IDS_PERMISSION_CONTROL:
        return DMPL_PRIV_CONTROL;
    }
    return 0;
}

static void SecurityDeleteUserList(SecurityState *state)
{
    while (state->userList)
    {
        UserAccessChange *next = state->userList->next;
        free(state->userList);
        state->userList = next;
    }
}

static HRESULT SecurityInitUserList(SecurityState *state)
{
    PDM_WALK_USERS walk = NULL;
    DWORD count = 0;
    HRESULT hr;

    hr = state->conn->HrOpenUserList(&walk, &count);
    if (FAILED(hr))
        return hr;

    while (count-- > 0)
    {
        UserAccessChange *entry = (UserAccessChange *)calloc(1, sizeof(UserAccessChange));
        if (!entry)
        {
            hr = E_OUTOFMEMORY;
            break;
        }
        hr = state->conn->HrWalkUserList(&walk, &entry->dmUser);
        if (FAILED(hr))
        {
            free(entry);
            break;
        }
        entry->dwNewAccess = entry->dmUser.AccessPrivileges;
        entry->next = state->userList;
        state->userList = entry;
    }

    state->conn->HrCloseUserList(walk);
    return hr;
}

static HRESULT SecurityInitSupport(SecurityState *state)
{
    HRESULT hr;
    char buffer[255];
    DWORD size = sizeof(buffer);

    hr = state->conn->HrIsSecurityEnabled(&state->fLocked);
    if (FAILED(hr))
        return hr;

    if (!state->fLocked)
    {
        hr = state->conn->HrSendCommand("GETUSERPRIV NAME=BOGUS", buffer, &size);
        if (hr == XBDM_INVALIDCMD)
            return S_FALSE;
    }
    else
    {
        hr = state->conn->HrGetUserAccess(NULL, &state->dwAccess);
        if (hr == XBDM_UNDEFINED)
        {
            char computerName[MAX_COMPUTERNAME_LENGTH + 1];
            DWORD cch = sizeof(computerName);
            GetComputerNameA(computerName, &cch);
            hr = state->conn->HrGetUserAccess(computerName, &state->dwAccess);
        }
        if (FAILED(hr))
            state->dwAccess = 0;
        if (state->dwAccess & DMPL_PRIV_MANAGE)
        {
            state->fManageMode = TRUE;
            SecurityInitUserList(state);
        }
    }
    return S_OK;
}

static void SecurityUpdateAccessInfo(SecurityState *state, LPCSTR userName, DWORD access, BOOL enable)
{
    char buffer[255];
    WindowUtils::rsprintf(buffer, ARRAYSIZE(buffer), IDS_SECURITY_PERMISSIONS_FOR, userName);
    SetWindowTextA(GetDlgItem(state->hDlg, IDC_SECURITY_ACCESS_TEXT), buffer);
    state->fUpdatingUI = TRUE;
    ListView_SetCheckState(state->hWndAccess, PERMISSION_LISTVIEW_INDEX(IDS_PERMISSION_READ), (access & DMPL_PRIV_READ) != 0);
    ListView_SetCheckState(state->hWndAccess, PERMISSION_LISTVIEW_INDEX(IDS_PERMISSION_WRITE), (access & DMPL_PRIV_WRITE) != 0);
    ListView_SetCheckState(state->hWndAccess, PERMISSION_LISTVIEW_INDEX(IDS_PERMISSION_CONFIGURE), (access & DMPL_PRIV_CONFIGURE) != 0);
    ListView_SetCheckState(state->hWndAccess, PERMISSION_LISTVIEW_INDEX(IDS_PERMISSION_CONTROL), (access & DMPL_PRIV_CONTROL) != 0);
    ListView_SetCheckState(state->hWndAccess, PERMISSION_LISTVIEW_INDEX(IDS_PERMISSION_MANAGE), (access & DMPL_PRIV_MANAGE) != 0);
    EnableWindow(state->hWndAccess, enable);
    state->fUpdatingUI = FALSE;
}

static void SecurityShowHideWindows(SecurityState *state)
{
    int lockedShow = state->fLocked ? SW_SHOW : SW_HIDE;
    int unlockShow = state->fLocked ? SW_HIDE : SW_SHOW;
    int manageShow = state->fManageMode ? SW_SHOW : SW_HIDE;
    int noManageShow = (state->fLocked && !state->fManageMode) ? SW_SHOW : SW_HIDE;

    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_UNLOCKED_TEXT), unlockShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_LOCK_BUTTON), unlockShow);
    ShowWindow(state->hWndAccess, lockedShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_ACCESS_TEXT), lockedShow);
    ShowWindow(state->hWndUsers, manageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_MACHINES), manageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_ADD), manageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_REMOVE), manageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_UNLOCK), manageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_CHANGE_PASSWORD), manageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_MANAGE_TEXT), noManageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_PASSWORD_TEXT), noManageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_PASSWORD_EDIT), noManageShow);
    ShowWindow(GetDlgItem(state->hDlg, IDC_SECURITY_MANAGE_BUTTON), noManageShow);
}

static void SecurityUpdateData(SecurityState *state)
{
    LVITEMA item = {0};
    int iItem = 0;

    ListView_DeleteAllItems(state->hWndAccess);
    ListView_DeleteAllItems(state->hWndUsers);

    if (!state->fLocked)
        return;

    if (state->fManageMode)
    {
        char accessName[60];
        int i;
        for (i = IDS_PERMISSION_READ; i <= IDS_PERMISSION_MANAGE; ++i)
        {
            LoadStringA(g_app.hInstance, i, accessName, sizeof(accessName));
            item.mask = LVIF_PARAM | LVIF_TEXT;
            item.iItem = PERMISSION_LISTVIEW_INDEX(i);
            item.iSubItem = 0;
            item.lParam = AccessBitFromResource(i);
            item.pszText = accessName;
            ListView_InsertItem(state->hWndAccess, &item);
        }

        UserAccessChange *user = state->userList;
        iItem = 0;
        while (user)
        {
            if (!(user->dwFlags & UACF_REMOVE))
            {
                item.mask = LVIF_PARAM | LVIF_TEXT;
                item.iItem = iItem++;
                item.iSubItem = 0;
                item.pszText = user->dmUser.UserName;
                item.lParam = (LPARAM)user;
                ListView_InsertItem(state->hWndUsers, &item);
            }
            user = user->next;
        }

        if (ListView_GetItemCount(state->hWndUsers) > 0)
        {
            ListView_SetItemState(state->hWndUsers, 0, LVIS_SELECTED, LVIS_SELECTED);
            state->iLastSelected = 0;
            item.mask = LVIF_PARAM;
            item.iItem = 0;
            ListView_GetItem(state->hWndUsers, &item);
            SecurityUpdateAccessInfo(state, ((UserAccessChange *)item.lParam)->dmUser.UserName,
                                     ((UserAccessChange *)item.lParam)->dwNewAccess, TRUE);
        }
    }
    else
    {
        char thisComputer[40];
        LoadStringA(g_app.hInstance, IDS_LITERAL_THIS_COMPUTER, thisComputer, sizeof(thisComputer));
        SecurityUpdateAccessInfo(state, thisComputer, state->dwAccess, FALSE);
    }
}

static void SecuritySetApply(SecurityState *state)
{
    BOOL enableApply = FALSE;
    UserAccessChange *user = state->userList;
    while (user)
    {
        if (UACF_ADDANDREMOVE != (UACF_ADDANDREMOVE & user->dwFlags))
        {
            if (user->dwFlags & UACF_ADDANDREMOVE)
            {
                enableApply = TRUE;
                break;
            }
            if (user->dmUser.AccessPrivileges != user->dwNewAccess)
            {
                enableApply = TRUE;
                break;
            }
        }
        user = user->next;
    }
    SendMessage(GetParent(state->hDlg), enableApply ? PSM_CHANGED : PSM_UNCHANGED, (WPARAM)state->hDlg, 0);
}

static INT_PTR CALLBACK UserNameDlgProc(HWND dlg, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_INITDIALOG)
    {
        SendDlgItemMessageA(dlg, IDC_XB_FILENAME, EM_SETLIMITTEXT, 79, 0);
        SetWindowLongPtrA(dlg, GWLP_USERDATA, lp);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(wp) == IDOK)
    {
        GetDlgItemTextA(dlg, IDC_XB_FILENAME, (LPSTR)GetWindowLongPtrA(dlg, GWLP_USERDATA), 255);
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(wp) == IDCANCEL)
    {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

static void SecurityAddUser(SecurityState *state)
{
    char userName[255];
    UserAccessChange *user;
    LVITEMA item = {0};
    int iItem;

    if (IDOK != DialogBoxParamA(g_app.hInstance, MAKEINTRESOURCEA(IDD_USERNAME_PROMPT), state->hDlg, UserNameDlgProc, (LPARAM)userName))
        return;

    for (user = state->userList; user; user = user->next)
    {
        if (_stricmp(user->dmUser.UserName, userName) == 0)
        {
            if (user->dwFlags & UACF_REMOVE)
                user->dwFlags &= ~UACF_REMOVE;
            else
                return;
            break;
        }
    }

    if (!user)
    {
        user = (UserAccessChange *)calloc(1, sizeof(UserAccessChange));
        if (!user)
            return;
        user->dwFlags = UACF_ADD;
        StringCchCopyA(user->dmUser.UserName, ARRAYSIZE(user->dmUser.UserName), userName);
        user->dwNewAccess = DMPL_PRIV_INITIAL;
        user->next = state->userList;
        state->userList = user;
    }

    item.mask = LVIF_PARAM | LVIF_TEXT;
    item.iItem = ListView_GetItemCount(state->hWndUsers);
    item.pszText = user->dmUser.UserName;
    item.lParam = (LPARAM)user;
    iItem = ListView_InsertItem(state->hWndUsers, &item);
    ListView_SetItemState(state->hWndUsers, iItem, LVIS_SELECTED, LVIS_SELECTED);
    state->iLastSelected = iItem;
    SecurityUpdateAccessInfo(state, user->dmUser.UserName, user->dwNewAccess, TRUE);
    SecuritySetApply(state);
}

static void SecurityRemoveUser(SecurityState *state)
{
    LVITEMA item = {0};
    int selected = ListView_GetNextItem(state->hWndUsers, -1, LVNI_SELECTED);
    UserAccessChange *user;

    if (selected < 0)
        return;

    item.iItem = selected;
    item.mask = LVIF_PARAM;
    ListView_GetItem(state->hWndUsers, &item);
    user = (UserAccessChange *)item.lParam;
    user->dwFlags |= UACF_REMOVE;
    ListView_DeleteItem(state->hWndUsers, selected);

    if (ListView_GetItemCount(state->hWndUsers) > 0)
    {
        if (selected >= ListView_GetItemCount(state->hWndUsers))
            selected--;
        ListView_SetItemState(state->hWndUsers, selected, LVIS_SELECTED, LVIS_SELECTED);
        item.iItem = selected;
        ListView_GetItem(state->hWndUsers, &item);
        SecurityUpdateAccessInfo(state, ((UserAccessChange *)item.lParam)->dmUser.UserName,
                                 ((UserAccessChange *)item.lParam)->dwNewAccess, TRUE);
    }
    SecuritySetApply(state);
}

static BOOL SecurityApplyChanges(SecurityState *state)
{
    UserAccessChange *user = state->userList;
    HRESULT hr = S_OK;
    BOOL someoneCanManage = FALSE;

    if (!state->fManageMode)
        return TRUE;

    while (user)
    {
        if (!(user->dwFlags & UACF_REMOVE) && (user->dwNewAccess & DMPL_PRIV_MANAGE))
        {
            someoneCanManage = TRUE;
            break;
        }
        user = user->next;
    }

    if (!someoneCanManage)
    {
        WindowUtils::MessageBoxResource(state->hDlg, IDS_CANNOT_REMOVE_LAST_MANAGER, IDS_GENERIC_CAPTION, MB_OK | MB_ICONSTOP);
        return FALSE;
    }

    state->conn->HrUseSharedConnection(TRUE);
    user = state->userList;
    while (user && SUCCEEDED(hr))
    {
        if (user->dwFlags & UACF_REMOVE)
        {
            if (!(user->dwFlags & UACF_ADD))
                hr = state->conn->HrRemoveUser(user->dmUser.UserName);
        }
        else if (user->dwFlags & UACF_ADD)
        {
            hr = state->conn->HrAddUser(user->dmUser.UserName, user->dwNewAccess);
            if (SUCCEEDED(hr))
                user->dwFlags &= ~UACF_ADD;
        }
        else if (user->dmUser.AccessPrivileges != user->dwNewAccess)
        {
            hr = state->conn->HrSetUserAccess(user->dmUser.UserName, user->dwNewAccess);
            if (SUCCEEDED(hr))
                user->dmUser.AccessPrivileges = user->dwNewAccess;
        }
        user = user->next;
    }
    state->conn->HrUseSharedConnection(FALSE);

    if (FAILED(hr))
    {
        WindowUtils::MessageBoxResource(state->hDlg, IDS_COULDNT_APPLY_SECURITY_CHANGES, IDS_GENERIC_CAPTION, MB_OK | MB_ICONSTOP);
        return FALSE;
    }
    SecuritySetApply(state);
    return TRUE;
}

static void SecurityUnlock(SecurityState *state)
{
    if (IDOK != WindowUtils::MessageBoxResource(state->hDlg, IDS_SECURITY_UNLOCK_WARNING, IDS_SECURITY_UNLOCK_WARNING_CAPTION,
                                                MB_OKCANCEL | MB_ICONINFORMATION, state->consoleName))
        return;

    HRESULT hr = state->conn->HrEnableSecurity(FALSE);
    if (SUCCEEDED(hr))
    {
        state->fLocked = FALSE;
        state->fManageMode = FALSE;
        SecurityDeleteUserList(state);
        SecurityShowHideWindows(state);
        SecurityUpdateData(state);
        SendMessage(GetParent(state->hDlg), PSM_UNCHANGED, (WPARAM)state->hDlg, 0);
    }
    else
    {
        ShowHresultError(state->hDlg, "RXDKNeighborhood", "Could not unlock console.", hr);
    }
}

static void SecurityLock(SecurityState *state)
{
    if (IDOK != WindowUtils::MessageBoxResource(state->hDlg, IDS_SECURITY_LOCK_WARNING, IDS_SECURITY_LOCK_WARNING_CAPTION,
                                                MB_OKCANCEL | MB_ICONINFORMATION, state->consoleName))
        return;

    HRESULT hr = state->conn->HrUseSharedConnection(TRUE);
    if (SUCCEEDED(hr))
    {
        hr = state->conn->HrEnableSecurity(TRUE);
        if (SUCCEEDED(hr))
        {
            char computerName[MAX_COMPUTERNAME_LENGTH + 1];
            DWORD cch = sizeof(computerName);
            GetComputerNameA(computerName, &cch);
            hr = state->conn->HrAddUser(computerName, DMPL_PRIV_ALL);
            if (SUCCEEDED(hr))
            {
                state->fLocked = TRUE;
                state->fManageMode = TRUE;
                SecurityInitUserList(state);
                SecurityShowHideWindows(state);
                SecurityUpdateData(state);
            }
        }
        state->conn->HrUseSharedConnection(FALSE);
    }
    if (FAILED(hr))
        ShowHresultError(state->hDlg, "RXDKNeighborhood", "Could not lock console.", hr);
}

static INT_PTR CALLBACK PasswordDlgProc(HWND dlg, UINT m, WPARAM wp, LPARAM lp)
{
    char confirm[MAX_PATH];
    if (m == WM_INITDIALOG)
    {
        SendDlgItemMessageA(dlg, IDC_SECURITY_PASSWORD_EDIT, EM_SETLIMITTEXT, 79, 0);
        SendDlgItemMessageA(dlg, IDC_SECURITY_CONFIRM_PASSWORD, EM_SETLIMITTEXT, 79, 0);
        SetWindowLongPtrA(dlg, GWLP_USERDATA, lp);
        return TRUE;
    }
    if (m == WM_COMMAND && LOWORD(wp) == IDOK)
    {
        char *password = (char *)GetWindowLongPtrA(dlg, GWLP_USERDATA);
        GetDlgItemTextA(dlg, IDC_SECURITY_PASSWORD_EDIT, password, MAX_PATH);
        GetDlgItemTextA(dlg, IDC_SECURITY_CONFIRM_PASSWORD, confirm, MAX_PATH);
        if (strcmp(password, confirm) != 0)
        {
            ShowWindow(GetDlgItem(dlg, IDC_SECURITY_PASSWORD_MISMATCH), SW_SHOW);
            return TRUE;
        }
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (m == WM_COMMAND && LOWORD(wp) == IDCANCEL)
    {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

static void SecurityChangePassword(SecurityState *state)
{
    char password[MAX_PATH] = "";
    if (IDOK == DialogBoxParamA(g_app.hInstance, MAKEINTRESOURCEA(IDD_PASSWORD_PROMPT), state->hDlg, PasswordDlgProc, (LPARAM)password))
    {
        HRESULT hr = state->conn->HrSetAdminPassword(password);
        if (SUCCEEDED(hr))
            WindowUtils::MessageBoxResource(state->hDlg, IDS_PASSWORD_SET, IDS_PASSWORD_SET_CAPTION, MB_OK, state->consoleName);
        else
            ShowHresultError(state->hDlg, "RXDKNeighborhood", "Could not set password.", hr);
    }
}

static void SecurityStartSecureMode(SecurityState *state)
{
    char password[MAX_PATH];
    HRESULT hr;

    GetDlgItemTextA(state->hDlg, IDC_SECURITY_PASSWORD_EDIT, password, MAX_PATH);
    hr = state->conn->HrUseSharedConnection(TRUE);
    if (SUCCEEDED(hr))
    {
        hr = state->conn->HrUseSecureConnection(password);
        if (SUCCEEDED(hr))
        {
            hr = state->conn->HrGetUserAccess(NULL, &state->dwAccess);
            if (hr == XBDM_UNDEFINED)
            {
                char computerName[MAX_COMPUTERNAME_LENGTH + 1];
                DWORD cch = sizeof(computerName);
                GetComputerNameA(computerName, &cch);
                hr = state->conn->HrGetUserAccess(computerName, &state->dwAccess);
            }
            if (state->dwAccess & DMPL_PRIV_MANAGE)
            {
                state->fManageMode = TRUE;
                SecurityInitUserList(state);
                SecurityShowHideWindows(state);
                SecurityUpdateData(state);
            }
        }
        state->conn->HrUseSharedConnection(FALSE);
    }
    if (FAILED(hr))
        ShowHresultError(state->hDlg, "RXDKNeighborhood", "Secure connection failed.", hr);
}

INT_PTR SecurityPage_DialogProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    SecurityState *state;

    if (msg == WM_INITDIALOG)
    {
        PropCtx *ctx = Properties_GetContext();
        LVCOLUMNA col = {0};

        state = (SecurityState *)calloc(1, sizeof(SecurityState));
        if (!state)
            return FALSE;

        state->ctx = ctx;
        state->conn = ctx->conn;
        state->hDlg = hDlg;
        StringCchCopyA(state->consoleName, ARRAYSIZE(state->consoleName), ctx->sel.consoleName);
        state->hWndAccess = GetDlgItem(hDlg, IDC_SECURITY_ACCESS);
        state->hWndUsers = GetDlgItem(hDlg, IDC_SECURITY_USERLIST);

        col.mask = LVCF_WIDTH;
        col.cx = 175;
        ListView_InsertColumn(state->hWndAccess, 0, &col);
        ListView_InsertColumn(state->hWndUsers, 0, &col);
        ListView_SetExtendedListViewStyle(state->hWndAccess, LVS_EX_CHECKBOXES);

        if (SUCCEEDED(SecurityInitSupport(state)))
        {
            SecurityShowHideWindows(state);
            SecurityUpdateData(state);
        }

        SetWindowLongPtrA(hDlg, DWLP_USER, (LONG_PTR)state);
        return TRUE;
    }

    state = (SecurityState *)GetWindowLongPtrA(hDlg, DWLP_USER);
    if (!state)
        return FALSE;

    switch (msg)
    {
    case WM_COMMAND:
        if (HIWORD(wParam) == BN_CLICKED)
        {
            switch (LOWORD(wParam))
            {
            case IDC_SECURITY_ADD:
                SecurityAddUser(state);
                return TRUE;
            case IDC_SECURITY_REMOVE:
                SecurityRemoveUser(state);
                return TRUE;
            case IDC_SECURITY_UNLOCK:
                SecurityUnlock(state);
                return TRUE;
            case IDC_SECURITY_CHANGE_PASSWORD:
                SecurityChangePassword(state);
                return TRUE;
            case IDC_SECURITY_LOCK_BUTTON:
                SecurityLock(state);
                return TRUE;
            case IDC_SECURITY_MANAGE_BUTTON:
                SecurityStartSecureMode(state);
                return TRUE;
            }
        }
        break;
    case WM_NOTIFY: {
        LPNMHDR hdr = (LPNMHDR)lParam;
        if (hdr->idFrom == IDC_SECURITY_ACCESS && hdr->code == LVN_ITEMCHANGED && !state->fUpdatingUI)
        {
            NMLISTVIEW *view = (NMLISTVIEW *)lParam;
            LVITEMA item = {0};
            int selected = ListView_GetNextItem(state->hWndUsers, -1, LVNI_SELECTED);
            UserAccessChange *user;

            if (selected < 0)
                return FALSE;
            item.iItem = selected;
            item.mask = LVIF_PARAM;
            ListView_GetItem(state->hWndUsers, &item);
            user = (UserAccessChange *)item.lParam;
            if (!user)
                return FALSE;

            if (ListView_GetCheckState(state->hWndAccess, view->iItem))
                user->dwNewAccess |= (DWORD)view->lParam;
            else
                user->dwNewAccess &= ~(DWORD)view->lParam;
            SecuritySetApply(state);
            return TRUE;
        }
        if (hdr->idFrom == IDC_SECURITY_USERLIST && hdr->code == NM_CLICK)
        {
            int selected = ListView_GetNextItem(state->hWndUsers, -1, LVNI_SELECTED);
            LVITEMA item = {0};
            if (selected >= 0)
            {
                state->iLastSelected = selected;
                item.iItem = selected;
                item.mask = LVIF_PARAM;
                ListView_GetItem(state->hWndUsers, &item);
                SecurityUpdateAccessInfo(state, ((UserAccessChange *)item.lParam)->dmUser.UserName,
                                         ((UserAccessChange *)item.lParam)->dwNewAccess, TRUE);
            }
            return TRUE;
        }
        if (hdr->code == PSN_APPLY)
        {
            if (!SecurityApplyChanges(state))
            {
                SetWindowLongPtrA(hDlg, DWLP_MSGRESULT, PSNRET_INVALID);
                return TRUE;
            }
            SetWindowLongPtrA(hDlg, DWLP_MSGRESULT, PSNRET_NOERROR);
            return TRUE;
        }
        break;
    }
    case WM_DESTROY:
        SecurityDeleteUserList(state);
        free(state);
        SetWindowLongPtrA(hDlg, DWLP_USER, 0);
        break;
    }
    return FALSE;
}
