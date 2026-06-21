#include "stdafx.h"

#include <initguid.h>

#include "ShellGuids.h"

// Public namespace coclass (native proxy registered in Explorer).
DEFINE_GUID(CLSID_XboxFolder,
    0xDB15FEDD, 0x96B8, 0x4DA9, 0x97, 0xE0, 0x7E, 0x5C, 0xCA, 0x05, 0xCC, 0x44);

// Managed implementation coclass (Rxdk.XbShellExt.comhost.dll).
DEFINE_GUID(CLSID_XboxFolderManaged,
    0xDB15FEDD, 0x96B8, 0x4DA9, 0x97, 0xE0, 0x7E, 0x5C, 0xCA, 0x05, 0xCC, 0x45);

// IShellFolderViewCB — the DefView callback interface. The correct IID is
// 2047E320-F2A9-11CE-AE65-08002B2E1262 (shlguid.h). A prior value (93F81976-…)
// was actually an undocumented interface Explorer probes via CreateViewObject;
// matching it there returned a view callback through the wrong vtable and
// corrupted the heap when navigating into a console/folder.
DEFINE_GUID(IID_IShellFolderViewCB,
    0x2047E320, 0xF2A9, 0x11CE, 0xAE, 0x65, 0x08, 0x00, 0x2B, 0x2E, 0x12, 0x62);
