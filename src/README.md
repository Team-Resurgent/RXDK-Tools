Host-tool **project sources** and `.vcxproj` files live under `src/`.

## Layout

| Path | Contents |
|------|----------|
| `../shared/` | Code and headers shared across projects |
| `../shared/include/` | `xboxdbg.h`, crypto headers, XBE image types |
| `../shared/common/` | Static-link helpers (`dm_error_shim.c`, `xboxdbg_static_init.c`, shell folder helpers) |
| `xboxdbg/` | Static debug monitor client (`xbdbgs.lib`) |
| `xbfile/` | Shared file helpers |
| `xbcp/`, `xbdir/`, `xbecopy/`, `xbmkdir/` | CLI file tools |
| `xbox-launch/` | Launch helper |
| `xbWatson/` | Break notification UI |
| `imagebld/` | Xbox Image File Builder (`imagebld.exe`; XBE signing via `xcbase.c` / `umkm.h`) |
| `xrsa/` | Source-built RSA/crypto library (`xrsa.lib`) |
| `xboxdbg-bridge/` | JSON debug host for DAP → `out/bin/x64/Release/xboxdbg-bridge.exe` |

## Build props

Each component folder includes its `.vcxproj` and optional `.props`. Shared output paths and roots (`IncludeRoot`, `CommonRoot`) live in **`RXDKTools.props`**. Every project also imports **`RXDKTools.SharedItems.props`** so `shared/include` and `shared/common` appear in Solution Explorer.

Set **`SharedCommonCompile`** in a project to compile specific common sources.
