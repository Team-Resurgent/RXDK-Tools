#pragma once

#include "neighborhood.h"

enum FileClipOp
{
    FileClipNone = 0,
    FileClipCut,
    FileClipCopy
};

void FileOps_ClearClipboard(void);
BOOL FileOps_HasClipboard(void);
FileClipOp FileOps_GetClipboardOp(void);

void FileOps_Cut(const SelectionInfo *sel);
void FileOps_Copy(const SelectionInfo *sel);
HRESULT FileOps_Paste(const SelectionInfo *target);
HRESULT FileOps_Delete(const SelectionInfo *sel);
HRESULT FileOps_Rename(const SelectionInfo *sel, LPCSTR newName);
HRESULT FileOps_NewFolder(const SelectionInfo *target);
HRESULT FileOps_LaunchXbe(const SelectionInfo *sel);
HRESULT FileOps_Reboot(const SelectionInfo *sel, BOOL cold, BOOL sameTitle);
HRESULT FileOps_ExportToPc(const SelectionInfo *sel);
HRESULT FileOps_ReceiveWireToLocal(IXboxConnection *conn, LPCSTR wirePath, LPCSTR localDir, LPCSTR name, BOOL isDir);
