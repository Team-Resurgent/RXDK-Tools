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



    HRESULT OpenShellNamespacePath(LPCSTR relativePathAnsi)
    {
        XB_TRACE_SCOPE("OpenShellNamespacePath");
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

        wchar_t shellPath[512] = {};
        if (relativePathAnsi == nullptr || relativePathAnsi[0] == '\0')
        {
            StringCchCopyW(shellPath, ARRAYSIZE(shellPath), L"shell:::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}");
        }
        else
        {
            wchar_t relativeWide[384] = {};
            MultiByteToWideChar(CP_ACP, 0, relativePathAnsi, -1, relativeWide, ARRAYSIZE(relativeWide));
            StringCchPrintfW(
                shellPath,
                ARRAYSIZE(shellPath),
                L"shell:::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}\\%s",
                relativeWide);
        }

        ShellTraceLine("ShellExecute shell:::");
        const HINSTANCE shellResult = ShellExecuteW(
            nullptr,
            nullptr,
            shellPath,
            nullptr,
            nullptr,
            SW_SHOWNORMAL);

        if (reinterpret_cast<INT_PTR>(shellResult) > 32)
        {
            ShellTraceLine("ShellExecute (shell:::) succeeded");
            return S_OK;
        }

        const HRESULT hrShellPath = HRESULT_FROM_WIN32(GetLastError());
        ShellTraceHr("ShellExecute shell:::", hrShellPath);

        wchar_t args[512] = {};
        if (relativePathAnsi == nullptr || relativePathAnsi[0] == '\0')
        {
            StringCchPrintfW(
                args,
                ARRAYSIZE(args),
                L"/e,\"/root,::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}\"");
        }
        else
        {
            wchar_t relativeWide[384] = {};
            MultiByteToWideChar(CP_ACP, 0, relativePathAnsi, -1, relativeWide, ARRAYSIZE(relativeWide));
            StringCchPrintfW(
                args,
                ARRAYSIZE(args),
                L"/e,\"/root,::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}\\%s\"",
                relativeWide);
        }

        const HINSTANCE result = ShellExecuteW(
            nullptr,
            L"open",
            L"explorer.exe",
            args,
            nullptr,
            SW_SHOWNORMAL);

        if (reinterpret_cast<INT_PTR>(result) > 32)
        {
            ShellTraceLine("ShellExecute (explorer.exe /e) succeeded");
            return S_OK;
        }

        const HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
        ShellTraceHr("ShellExecute explorer.exe /e", hr);
        return hr;
    }

    HRESULT OpenNamespaceViaShell()
    {
        return OpenShellNamespacePath(nullptr);
    }

    bool TryParseXboxUrl(LPCSTR cmdLine, char* relativePath, size_t relativePathChars)
    {
        if (!cmdLine || !relativePath || relativePathChars == 0)
            return false;

        relativePath[0] = '\0';

        while (*cmdLine == ' ' || *cmdLine == '\t')
            ++cmdLine;

        static constexpr char kProtocol[] = "xbox://";
        if (_strnicmp(cmdLine, kProtocol, sizeof(kProtocol) - 1) != 0)
            return false;

        cmdLine += sizeof(kProtocol) - 1;
        while (*cmdLine == '/')
            ++cmdLine;

        if (*cmdLine == '\0')
            return true;

        size_t write = 0;
        for (size_t i = 0; cmdLine[i] != '\0'; ++i)
        {
            char ch = cmdLine[i];
            if (ch == '/')
                ch = '\\';
            if (write + 1 >= relativePathChars)
                return false;
            relativePath[write++] = ch;
        }

        while (write > 0 && relativePath[write - 1] == '\\')
            --write;

        relativePath[write] = '\0';
        return true;
    }

}



extern "C" void CALLBACK OpenNamespace(HWND hwnd, HINSTANCE /*instance*/, LPSTR /*cmdLine*/, int /*showCmd*/)

{

    UNREFERENCED_PARAMETER(hwnd);

    const HRESULT hr = OpenNamespaceViaShell();

    if (FAILED(hr))

        ShowOpenFailure(hr);

}

extern "C" void CALLBACK LaunchExplorer(HWND hwnd, HINSTANCE /*instance*/, LPSTR cmdLine, int /*showCmd*/)
{
    UNREFERENCED_PARAMETER(hwnd);

    char relativePath[512] = {};
    if (!TryParseXboxUrl(cmdLine, relativePath, ARRAYSIZE(relativePath)))
    {
        MessageBoxA(
            hwnd,
            cmdLine ? cmdLine : "(null)",
            "Xbox Neighborhood - invalid xbox:// URL",
            MB_OK | MB_ICONERROR);
        return;
    }

    const HRESULT hr = OpenShellNamespacePath(relativePath[0] == '\0' ? nullptr : relativePath);
    if (FAILED(hr))
        ShowOpenFailure(hr);
}

