# xbshlext manual Explorer checklist

Use after `register-xbshlext-dev.ps1` or installing `XboxNeighborhood-Setup.exe`.

## Registration

- [ ] `status-xbshlext-dev.ps1` shows staged path matches registered `InprocServer32`
- [ ] Registered module is `Rxdk.XbShellExt.comhost.dll` (legacy installs may still show `xbshlext.dll`)
- [ ] `unregister-xbshlext-dev.ps1` removes namespace until re-registered

## Namespace

- [ ] `explorer.exe "shell:::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}"` opens Xbox Neighborhood
- [ ] Root lists configured consoles and **Add Xbox**
- [ ] Console folder lists kit drives
- [ ] Drive folders list XBDM directory entries

## Context menu

- [ ] **Properties** opens WinForms dialog (does not hang Explorer)
- [ ] **Add Xbox** opens wizard at namespace root / Add Xbox item
- [ ] **Security** opens Properties on Security tab for a console

## Staging layout

- [ ] All DLLs live beside `Rxdk.XbShellExt.comhost.dll` (same folder as `InprocServer32`)
- [ ] `Rxdk.XbShellExt.runtimeconfig.json`, `Rxdk.XbShellExt.deps.json`, and KitServices/Core dependencies present
- [ ] `console.ico` staged beside the comhost for shell folder icon
