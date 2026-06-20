# Managed .NET shell extension (Rxdk.XbShellExt)

## Architecture

Explorer loads `Rxdk.XbShellExt.comhost.dll` (CLSID `{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}`).

| Layer | Project |
|-------|---------|
| COM namespace (`IShellFolder`, context menu) | `Rxdk.XbShellExt` |
| Kit protocol + registry | `Rxdk.Xbdm.Managed`, `Rxdk.Xbdm.KitServices` |
| Properties / browser helpers | `RXDKNeighborhood.Core` |
| UI | WinForms in-process (STA thread) |
| Icons | `assets/shell/*.ico` |

No C++/CLI bridge, WinUI, or native `xbshlext.dll`.

## MVP status

- [x] Root folder lists consoles + “Add Xbox”
- [x] Console folder lists kit drives
- [x] Directory listing via XBDM
- [x] Default shell view (`SHCreateShellFolderView`)
- [x] Context menu: Properties, Add Xbox, Security (console)
- [x] WinForms Properties + Add Xbox wizard
- [ ] Drag/drop, copy, delete, rename
- [ ] Columns / details pane
- [ ] Icons (`IExtractIcon`)
- [ ] Full security editor
- [ ] Shortcut / `.xbox` protocol handler parity

## Dev workflow

```powershell
.\scripts\register-xbshlext-dev.ps1 -Force
explorer.exe "shell:::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}"
```

## Installer

`setup/build-installer.ps1` builds `Rxdk.XbShellExt` and produces `out/bin/x64/Release/XboxNeighborhood-Setup.exe`.

## Removed legacy stack

The following were removed from the build/install path during the managed rewrite:

- `src/xbshlext-bridge/` (C++/CLI)
- `Rxdk.XbShellExt.Interop`, `Rxdk.XbShellExt.UI`, and related TestHost projects

## Reference copy

`src/xbshlext/` may remain in the tree as the original native implementation (property sheets, drag/drop, columns, icons, etc.). It is **not** built or staged — use it for parity reference when extending `Rxdk.XbShellExt`. Icons live in `assets/shell/`.
