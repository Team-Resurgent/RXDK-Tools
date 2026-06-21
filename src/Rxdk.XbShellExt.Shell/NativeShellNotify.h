#pragma once

#include <shlobj.h>

void NotifyFolderContentsChanged(LPCITEMIDLIST folderPidl, LPCITEMIDLIST childPidl = nullptr);
bool ShouldRefreshFolderAfterContextCommand(int managedCommandId);
