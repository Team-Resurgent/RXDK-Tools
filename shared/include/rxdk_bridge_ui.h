#pragma once

#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

void __stdcall RxdkUi_ShowProperties(
    HWND owner,
    LPCSTR consoleName,
    LPCSTR folderPath,
    LPCSTR selectionSpec,
    LPCSTR initialTab);
void __stdcall RxdkUi_RunAddConsoleWizard(HWND owner);
void __stdcall RxdkUi_RunProgress(HWND owner, LPCSTR title);

#ifdef __cplusplus
}
#endif
