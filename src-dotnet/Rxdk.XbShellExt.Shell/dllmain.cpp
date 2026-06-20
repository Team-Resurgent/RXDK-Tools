#include "stdafx.h"
#include "FolderProxy.h"
#include "resource.h"
#include "ShellTrace.h"

CComModule _Module;

BEGIN_OBJECT_MAP(ObjectMap)
    OBJECT_ENTRY(CLSID_XboxFolder, CXboxFolderProxy)
END_OBJECT_MAP()

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        ShellTraceInit();
        ShellTraceSetModule(instance);
        ShellTraceInstallCrashLogger();
        ShellTraceLine("DllMain PROCESS_ATTACH module=%p", instance);
        _Module.Init(ObjectMap, instance, nullptr);
        DisableThreadLibraryCalls(instance);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        ShellTraceLine("DllMain PROCESS_DETACH");
        _Module.Term();
    }
    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return _Module.GetLockCount() == 0 ? S_OK : S_FALSE;
}

#include "ShellGuids.h"

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* ppv)
{
    ShellTraceInit();
    if (clsid == CLSID_XboxFolder)
        ShellTraceLine("DllGetClassObject CC44");
    const HRESULT hr = _Module.GetClassObject(clsid, riid, ppv);
    ShellTraceHr("DllGetClassObject", hr);
    return hr;
}

STDAPI DllRegisterServer()
{
    // Registry is written by scripts/Repair-XbShellExtRegistry.ps1.
    return S_OK;
}

STDAPI DllUnregisterServer()
{
    return S_OK;
}
