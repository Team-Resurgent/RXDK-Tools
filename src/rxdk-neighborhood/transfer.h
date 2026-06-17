#pragma once

#include "neighborhood.h"
#include <shellapi.h>

void Transfer_Init(HWND hwndList);
void Transfer_OnDropFiles(HDROP hDrop);
void Transfer_OnListBeginDrag(LPNMLISTVIEW nm);
