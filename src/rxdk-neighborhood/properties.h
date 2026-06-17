#pragma once

#include "neighborhood.h"

struct FilePropInfo
{
    DWORD fileCount;
    DWORD folderCount;
    ULONGLONG totalSize;
    DWORD validAttributes;
    DWORD attributes;
    FILETIME creationTime;
    FILETIME changeTime;
    char typeName[MAX_PATH];
    char location[MAX_PATH];
    BOOL prepared;
};

struct PropCtx
{
    SelectionInfo sel;
    IXboxConnection *conn;
    char driveDescription[MAX_CONSOLE_NAME + 16];
    UINT driveTypeResourceId;
    ULONGLONG totalSpace;
    ULONGLONG freeSpace;
    DWORD pieShadowHeight;
    FilePropInfo fileInfo;
};

void Properties_Show(const SelectionInfo *sel);
PropCtx *Properties_GetContext(void);
