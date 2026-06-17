#pragma once

#include "neighborhood.h"

namespace WindowUtils
{
int rsprintf(LPSTR pBuffer, size_t cchBuffer, UINT uFormatResource, ...);
int MessageBoxResource(HWND hWnd, UINT uTextResource, UINT uCaptionResource, UINT uType, ...);
void ReplaceWindowIcon(HWND hWnd, HICON hIcon);
HWND CreateWorkerWindow(HWND hWndParent);
LPCSTR GetPreloadedString(UINT uResourceId);
} // namespace WindowUtils

namespace FormatUtils
{
HRESULT XboxErrorString(HRESULT hr, LPSTR lpBuffer, int nBufferMax);
} // namespace FormatUtils
