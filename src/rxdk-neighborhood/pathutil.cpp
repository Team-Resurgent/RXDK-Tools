#include "neighborhood.h"
#include <strsafe.h>

BOOL BuildWirePath(LPCSTR displayPath, LPSTR wirePath, size_t cchWirePath)
{
    LPCSTR pszNext;

    if (!displayPath || !wirePath || cchWirePath < 4)
        return FALSE;

    pszNext = strchr(displayPath, '\\');
    if (!pszNext || !pszNext[1])
        return FALSE;

    pszNext++;

    if (FAILED(StringCchPrintfA(wirePath, cchWirePath, "%c:\\", *pszNext)))
        return FALSE;

    pszNext++;
    if (*pszNext == '\\')
        pszNext++;

    if (*pszNext)
    {
        if (FAILED(StringCchCatA(wirePath, cchWirePath, pszNext)))
            return FALSE;
    }

    return TRUE;
}

BOOL BuildWirePathInFolder(LPCSTR folderDisplayPath, LPCSTR name, LPSTR wirePath, size_t cchWirePath)
{
    char displayPath[MAX_PATH * 2];

    if (!folderDisplayPath || !name || !wirePath)
        return FALSE;

    if (FAILED(StringCchCopyA(displayPath, ARRAYSIZE(displayPath), folderDisplayPath)))
        return FALSE;

    if (!AppendDisplaySegment(displayPath, ARRAYSIZE(displayPath), name))
        return FALSE;

    return BuildWirePath(displayPath, wirePath, cchWirePath);
}

BOOL GetItemDisplayPath(LPCSTR folderPath, LPCSTR name, LPSTR itemPath, size_t cchItemPath)
{
    if (!folderPath || !name || !itemPath)
        return FALSE;

    if (FAILED(StringCchCopyA(itemPath, cchItemPath, folderPath)))
        return FALSE;

    return AppendDisplaySegment(itemPath, cchItemPath, name);
}

BOOL AppendDisplaySegment(LPSTR displayPath, size_t cchDisplayPath, LPCSTR segment)
{
    size_t len;

    if (!displayPath || !segment || !segment[0])
        return FALSE;

    len = strlen(displayPath);
    if (len > 0 && displayPath[len - 1] != '\\')
    {
        if (FAILED(StringCchCatA(displayPath, cchDisplayPath, "\\")))
            return FALSE;
    }

    return SUCCEEDED(StringCchCatA(displayPath, cchDisplayPath, segment));
}

BOOL GetParentDisplayPath(LPCSTR displayPath, LPSTR parentPath, size_t cchParentPath)
{
    char temp[MAX_PATH * 2];
    LPSTR slash;

    if (!displayPath || !parentPath)
        return FALSE;

    if (FAILED(StringCchCopyA(temp, ARRAYSIZE(temp), displayPath)))
        return FALSE;

    slash = strrchr(temp, '\\');
    if (!slash || slash == temp)
        return FALSE;

    *slash = '\0';
    return SUCCEEDED(StringCchCopyA(parentPath, cchParentPath, temp));
}

HRESULT GetConsoleConnection(LPCSTR consoleName, IXboxConnection **ppConnection)
{
    HRESULT hr;

    if (!consoleName || !consoleName[0] || !ppConnection)
        return E_INVALIDARG;

    *ppConnection = NULL;
    hr = DmGetXboxConnection(consoleName, XBCONN_VERSION, ppConnection);
    if (FAILED(hr))
        return hr;

    hr = (*ppConnection)->HrSetConnectionTimeout(10000, 30000);
    if (FAILED(hr))
    {
        (*ppConnection)->Release();
        *ppConnection = NULL;
    }
    return hr;
}

void ShowHresultError(HWND hwnd, LPCSTR caption, LPCSTR action, HRESULT hr)
{
    char err[256];
    char msg[512];

    if (SUCCEEDED(DmTranslateErrorA(hr, err, sizeof(err))))
        StringCchPrintfA(msg, ARRAYSIZE(msg), "%s\n\n%s", action, err);
    else
        StringCchPrintfA(msg, ARRAYSIZE(msg), "%s\n\nError 0x%08lX", action, (unsigned long)hr);

    MessageBoxA(hwnd, msg, caption, MB_OK | MB_ICONERROR);
}

void FormatFileTime(const FILETIME *pft, LPSTR buffer, size_t cchBuffer)
{
    SYSTEMTIME stLocal;

    buffer[0] = '\0';
    if (!pft)
        return;

    if (FileTimeToSystemTime(pft, &stLocal))
    {
        StringCchPrintfA(
            buffer,
            cchBuffer,
            "%04u-%02u-%02u %02u:%02u",
            stLocal.wYear,
            stLocal.wMonth,
            stLocal.wDay,
            stLocal.wHour,
            stLocal.wMinute);
    }
}

void FormatFileSize(ULONGLONG size, LPSTR buffer, size_t cchBuffer)
{
    if (size >= 1024ull * 1024ull * 1024ull)
        StringCchPrintfA(buffer, cchBuffer, "%.2f GB", size / (1024.0 * 1024.0 * 1024.0));
    else if (size >= 1024ull * 1024ull)
        StringCchPrintfA(buffer, cchBuffer, "%.2f MB", size / (1024.0 * 1024.0));
    else if (size >= 1024ull)
        StringCchPrintfA(buffer, cchBuffer, "%.2f KB", size / 1024.0);
    else
        StringCchPrintfA(buffer, cchBuffer, "%llu bytes", size);
}

static void FormatByteStringRecurse(ULONGLONG ullBytes, LPSTR *ppszNextChar)
{
    char szFormat[32];

    if (ullBytes > 1000)
    {
        FormatByteStringRecurse(ullBytes / 1000, ppszNextChar);
        if (LoadStringA(g_app.hInstance, IDS_PRELOAD_FILEBYTESIZE_FORMAT1, szFormat, sizeof(szFormat)))
            *ppszNextChar += wsprintfA(*ppszNextChar, szFormat, (ULONG)(ullBytes % 1000));
        return;
    }

    *ppszNextChar += wsprintfA(*ppszNextChar, "%d", (ULONG)ullBytes);
}

void FormatFileSizeBytes(ULONGLONG bytes, LPSTR buffer, size_t cchBuffer)
{
    char temp[64];
    char suffix[32];
    LPSTR pszWrite = temp;

    if (!buffer || cchBuffer == 0)
        return;

    temp[0] = '\0';
    FormatByteStringRecurse(bytes, &pszWrite);
    if (LoadStringA(g_app.hInstance, IDS_PRELOAD_FILEBYTESIZE_FORMAT2, suffix, sizeof(suffix)))
        strcpy(pszWrite, suffix);
    StringCchCopyA(buffer, cchBuffer, temp);
}
