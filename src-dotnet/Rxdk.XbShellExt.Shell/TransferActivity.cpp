#include "stdafx.h"
#include "TransferActivity.h"

namespace
{
    volatile long g_transferActivity = 0;
}

extern "C" void WINAPI XbShellExt_BeginTransferActivity()
{
    InterlockedIncrement(&g_transferActivity);
}

extern "C" void WINAPI XbShellExt_EndTransferActivity()
{
    InterlockedDecrement(&g_transferActivity);
}

long XbShellExt_GetTransferActivityCount()
{
    return g_transferActivity;
}
