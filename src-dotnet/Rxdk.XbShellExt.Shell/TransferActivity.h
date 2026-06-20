#pragma once

extern "C" void WINAPI XbShellExt_BeginTransferActivity();
extern "C" void WINAPI XbShellExt_EndTransferActivity();
long XbShellExt_GetTransferActivityCount();
