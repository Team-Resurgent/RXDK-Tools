#pragma once

#include <guiddef.h>

// Public namespace coclass (native proxy registered in Explorer).
extern "C" const GUID CLSID_XboxFolder;

// Managed implementation coclass (Rxdk.XbShellExt.comhost.dll).
extern "C" const GUID CLSID_XboxFolderManaged;

// {2047E320-F2A9-11CE-AE65-08002B2E1262} — IShellFolderViewCB (DefView callback).
extern "C" const GUID IID_IShellFolderViewCB;
