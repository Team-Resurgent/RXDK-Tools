#pragma once

#include <cstdarg>

void ShellTraceInit();
void ShellTraceClear();
void ShellTraceSetModule(void* moduleBase);
void ShellTraceInstallCrashLogger();
void ShellTraceLine(const char* fmt, ...);
void ShellTraceHr(const char* scope, HRESULT hr);
void ShellTraceGuid(const char* scope, REFIID riid);
void ShellTraceMsg(const char* scope, UINT uMsg);

class ShellTraceScope
{
public:
    explicit ShellTraceScope(const char* scope) : m_scope(scope)
    {
        ShellTraceLine(">> %s", m_scope);
    }

    ~ShellTraceScope()
    {
        ShellTraceLine("<< %s", m_scope);
    }

    void Note(const char* fmt, ...)
    {
        char body[512] = {};
        va_list args;
        va_start(args, fmt);
        vsnprintf(body, sizeof(body), fmt, args);
        va_end(args);
        ShellTraceLine(".. %s %s", m_scope, body);
    }

private:
    const char* m_scope;
};

#define XB_TRACE_SCOPE(name) ShellTraceScope __xbTraceScope(name)
