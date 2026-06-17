#include "neighborhood.h"
#include <stdio.h>
#include <strsafe.h>

static HKEY OpenConsoleKey(void)
{
    HKEY hKey = NULL;
    if (RegCreateKeyExA(
            HKEY_CURRENT_USER,
            REG_CONSOLE_ROOT,
            0,
            NULL,
            REG_OPTION_NON_VOLATILE,
            KEY_ALL_ACCESS,
            NULL,
            &hKey,
            NULL) != ERROR_SUCCESS)
    {
        return NULL;
    }
    return hKey;
}

static void EnsureDefaultConsoleAdded(HKEY hKey)
{
    char defaultName[80];
    DWORD cch = sizeof(defaultName);
    if (SUCCEEDED(DmGetXboxName(defaultName, &cch)) && defaultName[0])
    {
        DWORD dummy = 0;
        RegSetValueExA(hKey, defaultName, 0, REG_DWORD, (const BYTE *)&dummy, sizeof(dummy));
    }
}

BOOL Consoles_Add(LPCSTR name)
{
    HKEY hKey;
    DWORD dummy = 0;

    if (!name || !name[0])
        return FALSE;

    hKey = OpenConsoleKey();
    if (!hKey)
        return FALSE;

    EnsureDefaultConsoleAdded(hKey);
    RegSetValueExA(hKey, name, 0, REG_DWORD, (const BYTE *)&dummy, sizeof(dummy));
    RegCloseKey(hKey);
    return TRUE;
}

BOOL Consoles_Remove(LPCSTR name)
{
    HKEY hKey;
    char defaultName[80];
    DWORD cch;

    if (!name || !name[0])
        return FALSE;

    cch = sizeof(defaultName);
    if (SUCCEEDED(DmGetXboxName(defaultName, &cch)) && _stricmp(name, defaultName) == 0)
        return FALSE;

    hKey = OpenConsoleKey();
    if (!hKey)
        return FALSE;

    RegDeleteValueA(hKey, name);
    RegCloseKey(hKey);
    return TRUE;
}

BOOL Consoles_SetDefault(LPCSTR name)
{
    if (!name || !name[0])
        return FALSE;
    if (!Consoles_Add(name))
        return FALSE;
    return SUCCEEDED(DmSetXboxName(name));
}

BOOL Consoles_GetDefault(LPSTR name, DWORD cchName)
{
    if (!name || cchName == 0)
        return FALSE;
    return SUCCEEDED(DmGetXboxName(name, &cchName));
}

BOOL Consoles_IsKnown(LPCSTR name)
{
    HKEY hKey;
    DWORD dummy;
    DWORD cb = sizeof(dummy);
    LONG err;

    if (!name || !name[0])
        return FALSE;

    hKey = OpenConsoleKey();
    if (!hKey)
        return FALSE;

    err = RegQueryValueExA(hKey, name, NULL, NULL, (BYTE *)&dummy, &cb);
    RegCloseKey(hKey);
    return err == ERROR_SUCCESS;
}

void Consoles_Enumerate(BOOL (*callback)(LPCSTR name, void *ctx), void *ctx)
{
    HKEY hKey;
    char name[256];
    DWORD nameLen;
    DWORD index = 0;

    if (!callback)
        return;

    hKey = OpenConsoleKey();
    if (!hKey)
        return;

    EnsureDefaultConsoleAdded(hKey);

    for (;;)
    {
        nameLen = sizeof(name);
        if (RegEnumValueA(hKey, index++, name, &nameLen, NULL, NULL, NULL, NULL) != ERROR_SUCCESS)
            break;
        if (name[0] == '\0')
            continue;
        if (!callback(name, ctx))
            break;
    }

    RegCloseKey(hKey);
}
