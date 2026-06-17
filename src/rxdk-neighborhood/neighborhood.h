#pragma once

#define _WIN32_IE 0x0600
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commctrl.h>
#include <commdlg.h>
#include <shlobj.h>
#include <xboxdbg.h>
#include <ixbconn.h>
#include <stdio.h>

#ifndef ARRAYSIZE
#define ARRAYSIZE(a) (sizeof(a) / sizeof((a)[0]))
#endif

#define REG_CONSOLE_ROOT "Software\\Microsoft\\XboxSDK\\RXDKNeighborhood\\Consoles"
#define MAX_CONSOLE_NAME 80
#define MAX_SEL_ITEMS 64

#ifndef _ASSERTE
#define _ASSERTE(expr) ((void)0)
#endif

#include "resource.h"

enum NodeKind
{
    NodeRoot = 1,
    NodeConsole,
    NodeDrive,
    NodeFolder
};

struct NodeInfo
{
    NodeKind kind;
    char displayPath[MAX_PATH * 2];
    char consoleName[80];
};

enum SelKind
{
    SelNone = 0,
    SelRoot,
    SelConsole,
    SelDrive,
    SelFolder,
    SelFile,
    SelFiles
};

struct SelectedItem
{
    char name[256];
    char displayPath[MAX_PATH * 2];
    char wirePath[MAX_PATH];
    DM_FILE_ATTRIBUTES attrs;
    BOOL hasAttrs;
};

struct SelectionInfo
{
    SelKind kind;
    char consoleName[80];
    char folderPath[MAX_PATH * 2];
    SelectedItem items[MAX_SEL_ITEMS];
    int itemCount;
};

struct AppState
{
    HINSTANCE hInstance;
    HWND hwndMain;
    HWND hwndTree;
    HWND hwndList;
    HWND hwndStatus;
    char currentPath[MAX_PATH * 2];
    char currentConsole[80];
};

extern AppState g_app;

BOOL Consoles_Add(LPCSTR name);
BOOL Consoles_Remove(LPCSTR name);
BOOL Consoles_SetDefault(LPCSTR name);
BOOL Consoles_GetDefault(LPSTR name, DWORD cchName);
BOOL Consoles_IsKnown(LPCSTR name);
void Consoles_Enumerate(BOOL (*callback)(LPCSTR name, void *ctx), void *ctx);

BOOL BuildWirePath(LPCSTR displayPath, LPSTR wirePath, size_t cchWirePath);
BOOL BuildWirePathInFolder(LPCSTR folderDisplayPath, LPCSTR name, LPSTR wirePath, size_t cchWirePath);
BOOL AppendDisplaySegment(LPSTR displayPath, size_t cchDisplayPath, LPCSTR segment);
BOOL GetParentDisplayPath(LPCSTR displayPath, LPSTR parentPath, size_t cchParentPath);
BOOL GetItemDisplayPath(LPCSTR folderPath, LPCSTR name, LPSTR itemPath, size_t cchItemPath);

HRESULT GetConsoleConnection(LPCSTR consoleName, IXboxConnection **ppConnection);
void ShowHresultError(HWND hwnd, LPCSTR caption, LPCSTR action, HRESULT hr);
void FormatFileTime(const FILETIME *pft, LPSTR buffer, size_t cchBuffer);
void FormatFileSize(ULONGLONG size, LPSTR buffer, size_t cchBuffer);
void FormatFileSizeBytes(ULONGLONG bytes, LPSTR buffer, size_t cchBuffer);

void AppRefreshTree(void);
void AppRefreshCurrentView(void);
BOOL AppBuildSelection(SelectionInfo *sel);
BOOL AppFillFolderTarget(SelectionInfo *sel);
BOOL AppIsDriveListing(void);
BOOL AppIsFolderView(void);
void AppOpenDisplayPath(LPCSTR displayPath);
HINSTANCE AppInstance(void);

inline LONG_PTR XdkSetDlgUser(HWND hwnd, LONG_PTR val)
{
    return SetWindowLongPtrA(hwnd, DWLP_USER, val);
}
inline LONG_PTR XdkGetDlgUser(HWND hwnd)
{
    return GetWindowLongPtrA(hwnd, DWLP_USER);
}
inline LONG_PTR XdkSetDlgMsgResult(HWND hwnd, LONG_PTR val)
{
    return SetWindowLongPtrA(hwnd, DWLP_MSGRESULT, val);
}
