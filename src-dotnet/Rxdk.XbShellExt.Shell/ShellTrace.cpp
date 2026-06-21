#include "stdafx.h"
#include "ShellTrace.h"

#include <cstdio>
#include <cstdarg>

namespace
{
    CRITICAL_SECTION g_traceLock;
    bool g_traceReady = false;

    constexpr wchar_t kLogFolderName[] = L"Xbox Neighborhood\\Logs";
    constexpr wchar_t kNativeLogName[] = L"xb-shlext.log";

    wchar_t g_tracePath[MAX_PATH] = {};
    bool g_tracePathReady = false;

    void* g_moduleBase = nullptr;
    LONG g_crashLogged = 0;

    bool ShellTraceEnabled()
    {
        static int state = -1;
        if (state < 0)
        {
            wchar_t buf[16] = {};
            const DWORD n = GetEnvironmentVariableW(L"XB_SHLEXT_TRACE", buf, _countof(buf));
            if (n > 0 &&
                (buf[0] == L'0' ||
                 _wcsicmp(buf, L"false") == 0 ||
                 _wcsicmp(buf, L"off") == 0))
            {
                state = 0;
            }
            else
            {
                state = 1;
            }
        }

        return state != 0;
    }

    bool EnsureTracePath()
    {
        if (g_tracePathReady)
            return g_tracePath[0] != L'\0';

        g_tracePathReady = true;
        g_tracePath[0] = L'\0';

        wchar_t programData[MAX_PATH] = {};
        if (FAILED(SHGetFolderPathW(nullptr, CSIDL_COMMON_APPDATA, nullptr, SHGFP_TYPE_CURRENT, programData)))
            return false;

        wchar_t logDir[MAX_PATH] = {};
        if (FAILED(StringCchPrintfW(logDir, _countof(logDir), L"%s\\%s", programData, kLogFolderName)))
            return false;

        SHCreateDirectoryExW(nullptr, logDir, nullptr);

        if (FAILED(StringCchPrintfW(g_tracePath, _countof(g_tracePath), L"%s\\%s", logDir, kNativeLogName)))
        {
            g_tracePath[0] = L'\0';
            return false;
        }

        return true;
    }

    void WriteRaw(const char* text)
    {
        if (!text || !EnsureTracePath())
            return;

        HANDLE file = CreateFileW(
            g_tracePath,
            FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (file == INVALID_HANDLE_VALUE)
            return;

        DWORD written = 0;
        WriteFile(file, text, static_cast<DWORD>(strlen(text)), &written, nullptr);
        CloseHandle(file);
    }

    void WriteProcessBanner()
    {
        wchar_t path[MAX_PATH] = {};
        DWORD size = MAX_PATH;
        const HANDLE process = GetCurrentProcess();
        if (QueryFullProcessImageNameW(process, 0, path, &size))
        {
            char narrow[MAX_PATH] = {};
            WideCharToMultiByte(CP_UTF8, 0, path, -1, narrow, static_cast<int>(sizeof(narrow)), nullptr, nullptr);
            ShellTraceLine("process=%s", narrow);
            return;
        }

        if (GetModuleFileNameW(nullptr, path, MAX_PATH))
        {
            char narrow[MAX_PATH] = {};
            WideCharToMultiByte(CP_UTF8, 0, path, -1, narrow, static_cast<int>(sizeof(narrow)), nullptr, nullptr);
            ShellTraceLine("process=%s", narrow);
        }
    }
}

void ShellTraceClear()
{
    if (!ShellTraceEnabled() || !EnsureTracePath())
        return;

    HANDLE file = CreateFileW(
        g_tracePath,
        GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (file != INVALID_HANDLE_VALUE)
        CloseHandle(file);
}

void ShellTraceInit()
{
    if (!ShellTraceEnabled() || g_traceReady)
        return;

    InitializeCriticalSection(&g_traceLock);
    g_traceReady = true;
    ShellTraceLine("=== Rxdk.XbShellExt.Shell load pid=%lu ===", GetCurrentProcessId());
    WriteProcessBanner();
}

void ShellTraceSetModule(void* moduleBase)
{
    if (!ShellTraceEnabled())
        return;

    g_moduleBase = moduleBase;
}

namespace
{
    LONG CALLBACK CrashVectoredHandler(EXCEPTION_POINTERS* info)
    {
        if (!ShellTraceEnabled() || !info || !info->ExceptionRecord)
            return EXCEPTION_CONTINUE_SEARCH;

        const DWORD code = info->ExceptionRecord->ExceptionCode;
        const bool fatal =
            code == EXCEPTION_ACCESS_VIOLATION ||
            code == EXCEPTION_STACK_OVERFLOW ||
            code == EXCEPTION_ILLEGAL_INSTRUCTION ||
            code == EXCEPTION_INT_DIVIDE_BY_ZERO ||
            code == 0xC0000409 /* fail fast / stack buffer overrun */ ||
            code == 0xC0000374 /* heap corruption */;
        if (!fatal)
            return EXCEPTION_CONTINUE_SEARCH;

        // Only log the first few to avoid flooding (handlers fire on first-chance too).
        if (InterlockedIncrement(&g_crashLogged) > 6)
            return EXCEPTION_CONTINUE_SEARCH;

        const void* addr = info->ExceptionRecord->ExceptionAddress;
        if (g_moduleBase && addr >= g_moduleBase)
        {
            const auto offset = static_cast<unsigned long long>(
                reinterpret_cast<const unsigned char*>(addr) -
                reinterpret_cast<const unsigned char*>(g_moduleBase));
            ShellTraceLine(
                "!! EXCEPTION code=0x%08X addr=%p base=%p offset=0x%llX",
                code, addr, g_moduleBase, offset);
        }
        else
        {
            ShellTraceLine("!! EXCEPTION code=0x%08X addr=%p (outside our module)", code, addr);
        }
        return EXCEPTION_CONTINUE_SEARCH;
    }
}

void ShellTraceInstallCrashLogger()
{
    if (!ShellTraceEnabled())
        return;

    static bool installed = false;
    if (installed)
        return;
    installed = true;
    AddVectoredExceptionHandler(0, CrashVectoredHandler);
}

void ShellTraceLine(const char* fmt, ...)
{
    if (!fmt || !ShellTraceEnabled())
        return;

    if (!g_traceReady)
        ShellTraceInit();

    char body[1024] = {};
    va_list args;
    va_start(args, fmt);
    vsnprintf(body, sizeof(body), fmt, args);
    va_end(args);

    char line[1200] = {};
    SYSTEMTIME st = {};
    GetLocalTime(&st);
    snprintf(
        line,
        sizeof(line),
        "%02u:%02u:%02u.%03u tid=%lu %s\r\n",
        st.wHour,
        st.wMinute,
        st.wSecond,
        st.wMilliseconds,
        GetCurrentThreadId(),
        body);

    EnterCriticalSection(&g_traceLock);
    WriteRaw(line);
    LeaveCriticalSection(&g_traceLock);
}

void ShellTraceHr(const char* scope, HRESULT hr)
{
    if (!ShellTraceEnabled())
        return;

    ShellTraceLine("%s hr=0x%08X", scope, static_cast<unsigned>(hr));
}

void ShellTraceGuid(const char* scope, REFIID riid)
{
    if (!ShellTraceEnabled())
        return;

    LPOLESTR iidStr = nullptr;
    if (SUCCEEDED(StringFromCLSID(riid, &iidStr)) && iidStr)
    {
        char narrow[128] = {};
        WideCharToMultiByte(CP_UTF8, 0, iidStr, -1, narrow, static_cast<int>(sizeof(narrow)), nullptr, nullptr);
        ShellTraceLine("%s riid=%s", scope, narrow);
        CoTaskMemFree(iidStr);
    }
    else
    {
        ShellTraceLine("%s riid=(unknown)", scope);
    }
}

void ShellTraceMsg(const char* scope, UINT uMsg)
{
    if (!ShellTraceEnabled())
        return;

    ShellTraceLine("%s uMsg=%u", scope, uMsg);
}
