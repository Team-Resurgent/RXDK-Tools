#include "utils.h"
#include <stdarg.h>
#include <strsafe.h>

#ifndef FACILITY_XBDM
#define FACILITY_XBDM 0x0BD
#endif

static UINT FGetErrorStringResourceId(HRESULT hr)
{
    switch (hr)
    {
    case E_FAIL:
    case E_UNEXPECTED:
        return IDC_E_UNEXPECTED;
    case E_INVALIDARG:
        return IDC_E_INVALIDARG;
    default:
        if (FAILED(hr) && HRESULT_FACILITY(hr) == FACILITY_XBDM)
            return 0x8000 | (hr & 0xfff);
        break;
    }
    return 0;
}

int WindowUtils::rsprintf(LPSTR pBuffer, size_t cchBuffer, UINT uFormatResource, ...)
{
    int i = 0;
    va_list vl;
    char szFormat[512];

    if (!pBuffer || cchBuffer == 0)
        return 0;

    pBuffer[0] = '\0';
    if (LoadStringA(g_app.hInstance, uFormatResource, szFormat, sizeof(szFormat)))
    {
        va_start(vl, uFormatResource);
        i = StringCchVPrintfA(pBuffer, cchBuffer, szFormat, vl);
        va_end(vl);
    }
    return i;
}

int WindowUtils::MessageBoxResource(HWND hWnd, UINT uTextResource, UINT uCaptionResource, UINT uType, ...)
{
    char szFormat[1024];
    char szText[1024];
    char szCaption[128];
    va_list vl;

    if (!LoadStringA(g_app.hInstance, uCaptionResource, szCaption, sizeof(szCaption)))
        StringCchCopyA(szCaption, ARRAYSIZE(szCaption), "RXDKNeighborhood");

    if (LoadStringA(g_app.hInstance, uTextResource, szFormat, sizeof(szFormat)))
    {
        va_start(vl, uType);
        wvsprintfA(szText, szFormat, vl);
        va_end(vl);
    }
    else
    {
        StringCchCopyA(szText, ARRAYSIZE(szText), "An error occurred.");
    }

    return MessageBoxA(hWnd, szText, szCaption, uType);
}

void WindowUtils::ReplaceWindowIcon(HWND hWnd, HICON hIcon)
{
    SendMessageA(hWnd, STM_SETICON, (WPARAM)hIcon, 0);
}

static LRESULT CALLBACK WorkerWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    return DefWindowProcA(hwnd, msg, wParam, lParam);
}

HWND WindowUtils::CreateWorkerWindow(HWND hWndParent)
{
    static ATOM s_atom = 0;
    if (!s_atom)
    {
        WNDCLASSEXA wc = {sizeof(wc)};
        wc.lpfnWndProc = WorkerWndProc;
        wc.hInstance = g_app.hInstance;
        wc.lpszClassName = "RXDKNeighborhoodWorker";
        s_atom = RegisterClassExA(&wc);
        if (!s_atom)
            return NULL;
    }
    return CreateWindowExA(0, "RXDKNeighborhoodWorker", "", WS_POPUP, 0, 0, 0, 0, hWndParent, NULL, g_app.hInstance, NULL);
}

LPCSTR WindowUtils::GetPreloadedString(UINT uResourceId)
{
    static char s_buffers[8][128];
    static int s_index = 0;
    char *buf = s_buffers[s_index++ % 8];
    LoadStringA(g_app.hInstance, uResourceId, buf, sizeof(s_buffers[0]));
    return buf;
}

HRESULT FormatUtils::XboxErrorString(HRESULT hr, LPSTR lpBuffer, int nBufferMax)
{
    UINT resourceId;
    int len = 0;

    if (!lpBuffer || nBufferMax <= 0)
        return E_INVALIDARG;

    lpBuffer[0] = '\0';

    if (HRESULT_FACILITY(hr) == FACILITY_XBDM)
    {
        resourceId = FGetErrorStringResourceId(hr);
        if (resourceId == 0)
            resourceId = IDC_XBDM_NOERRORSTRING;
        len = LoadStringA(g_app.hInstance, resourceId, lpBuffer, nBufferMax);
    }
    else
    {
        if (!FormatMessageA(FORMAT_MESSAGE_FROM_SYSTEM, NULL, hr, 0, lpBuffer, nBufferMax, NULL))
            len = LoadStringA(g_app.hInstance, IDC_E_UNEXPECTED, lpBuffer, nBufferMax);
    }

    return len ? S_OK : HRESULT_FROM_WIN32(GetLastError());
}
