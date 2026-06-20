#include "stdafx.h"
#include "ShellGuids.h"
#include "ShellTrace.h"

namespace
{
    void ShowOpenFailure(HRESULT hr)

    {

        wchar_t message[256];

        StringCchPrintfW(

            message,

            ARRAYSIZE(message),

            L"Could not open Xbox Neighborhood (HRESULT 0x%08X).",

            static_cast<unsigned>(hr));

        MessageBoxW(nullptr, message, L"Xbox Neighborhood", MB_OK | MB_ICONERROR);

    }



    bool IsProcessElevated()

    {

        HANDLE token = nullptr;

        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))

            return false;



        TOKEN_ELEVATION elevation = {};

        DWORD size = 0;

        const BOOL ok = GetTokenInformation(token, TokenElevation, &elevation, sizeof(elevation), &size);

        CloseHandle(token);

        return ok && elevation.TokenIsElevated != 0;

    }



    HRESULT OpenNamespaceViaShell()
    {
        XB_TRACE_SCOPE("OpenNamespaceViaShell");
        if (IsProcessElevated())

        {

            MessageBoxW(

                nullptr,

                L"Do not open Xbox Neighborhood from an elevated process.\r\n"

                L"Close this window and run open-xbox-neighborhood.cmd without admin.",

                L"Xbox Neighborhood",

                MB_OK | MB_ICONWARNING);

            return HRESULT_FROM_WIN32(ERROR_ELEVATION_REQUIRED);

        }



        // Win10/11 hide namespace folders in the nav pane unless this is set.
        {
            HKEY advancedKey = nullptr;
            if (RegCreateKeyExW(
                    HKEY_CURRENT_USER,
                    L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
                    0,
                    nullptr,
                    0,
                    KEY_SET_VALUE,
                    nullptr,
                    &advancedKey,
                    nullptr) == ERROR_SUCCESS)
            {
                constexpr DWORD showAll = 1;
                RegSetValueExW(advancedKey, L"NavPaneShowAllFolders", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&showAll), sizeof(showAll));
                RegCloseKey(advancedKey);
            }
        }

        // Open Explorer in folder-tree mode (/e,/root) so the namespace appears in the
        // side navigation pane like the legacy Xbox Neighborhood view.
        const HINSTANCE result = ShellExecuteW(
            nullptr,
            L"open",
            L"explorer.exe",
            L"/e,/root,::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}",
            nullptr,
            SW_SHOWNORMAL);

        if (reinterpret_cast<INT_PTR>(result) > 32)
        {
            ShellTraceLine("ShellExecute (explorer.exe /e) succeeded");
            return S_OK;
        }

        const HRESULT hrShell = HRESULT_FROM_WIN32(GetLastError());
        ShellTraceHr("ShellExecute explorer.exe /e", hrShell);

        // Fallback: shell:: binding without an explicit Explorer frame.
        ShellTraceLine(".. fallback shell:::");
        const HINSTANCE fallback = ShellExecuteW(
            nullptr,
            nullptr,
            L"shell:::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}",
            nullptr,
            nullptr,
            SW_SHOWNORMAL);

        if (reinterpret_cast<INT_PTR>(fallback) > 32)
        {
            ShellTraceLine("ShellExecute (shell:::) succeeded");
            return S_OK;
        }

        const HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
        ShellTraceHr("ShellExecute shell:::", hr);
        return hr;

    }

}



extern "C" void CALLBACK OpenNamespace(HWND hwnd, HINSTANCE /*instance*/, LPSTR /*cmdLine*/, int /*showCmd*/)

{

    UNREFERENCED_PARAMETER(hwnd);

    const HRESULT hr = OpenNamespaceViaShell();

    if (FAILED(hr))

        ShowOpenFailure(hr);

}

