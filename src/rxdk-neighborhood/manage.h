#pragma once

#include "neighborhood.h"

class CManageConsoles
{
  public:
    CManageConsoles()
    {
    }

    BOOL Add(LPSTR pszConsoleName)
    {
        return Consoles_Add(pszConsoleName);
    }

    BOOL Remove(LPSTR pszConsoleName)
    {
        return Consoles_Remove(pszConsoleName);
    }

    BOOL SetDefault(LPSTR pszConsoleName)
    {
        return Consoles_SetDefault(pszConsoleName);
    }

    BOOL IsDefault(LPSTR pszConsoleName)
    {
        char defaultName[80];
        if (!Consoles_GetDefault(defaultName, sizeof(defaultName)))
            return FALSE;
        return _stricmp(defaultName, pszConsoleName) == 0;
    }

    void ResetEnum()
    {
        m_index = 0;
    }

    BOOL GetNext(LPSTR pszConsoleName, DWORD *pdwConsoleNameLength)
    {
        (void)pdwConsoleNameLength;
        return FALSE;
    }

    DWORD GetMaxCount()
    {
        return 0;
    }

  private:
    DWORD m_index;
};
