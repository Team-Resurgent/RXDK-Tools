# RXDK Tools

<p align="center">
  <a href="https://discord.gg/VcdSfajQGK"><img src="https://img.shields.io/badge/chat-on%20discord-7289da.svg?logo=discord" alt="Discord"></a>
  &nbsp;
  <a href="https://ko-fi.com/J3J7L5UMN"><img src="https://ko-fi.com/img/githubbutton_sm.svg" alt="ko-fi"></a>
  &nbsp;
  <a href="https://www.patreon.com/teamresurgent"><img src="https://img.shields.io/badge/Patreon-F96854?style=for-the-badge&logo=patreon&logoColor=white" alt="Patreon"></a>
</p>

**Recompiled Original Xbox XDK host tools for modern Windows, Linux, and macOS.**

The classic XDK shipped 32-bit utilities that no longer run on modern Windows. RXDK Tools rebuilds them as **managed .NET 8** ports: cross-platform CLI tools, a Windows shell extension, and standalone Avalonia apps. **Xbox Neighborhood** is a managed **`Rxdk.XbShellExt`** COM shell extension over **`Rxdk.Xbdm.Managed`**. All console-facing tools talk to kits over the **Xbox Debug Monitor (XBDM)** protocol.

**What's included**

| Category | Tools |
|----------|-------|
| Explorer (Windows) | **Xbox Neighborhood** shell extension (`Rxdk.XbShellExt.comhost.dll`) |
| Neighborhood app | **`xbneighborhood.exe`** — Avalonia standalone browser |
| Console config | **`xbset`** — set default kit for CLI tools |
| File transfer | `xbcp`, `xbdir`, `xbmkdir`, `xbecopy` |
| Build | `imagebld` (PE → signed `.xbe`) |
| Debug | `xbox-launch`, **`xbwatson`**, `xboxdbg-bridge` |

## Contents

- [Requirements](#requirements)
- [Quick start — Xbox Neighborhood (shell extension)](#quick-start--xbox-neighborhood-shell-extension)
- [Quick start — Rxdk.XbNeighborhood app](#quick-start--rxdkxbneighborhood-app)
- [Screenshots](#screenshots)
- [Tools](#tools)
  - [Xbox Neighborhood](#xbox-neighborhood-rxdkxbshlext)
  - [Rxdk.XbNeighborhood app](#rxdkxbneighborhood-app)
  - [Managed CLI tools](#managed-cli-tools-cross-platform)
  - [Default console (`xbset`)](#default-console-xbset)
  - [File utilities](#file-utilities-xbcp-xbdir-xbmkdir-xbecopy)
  - [Image builder](#xbox-image-file-builder-imagebld)
  - [Launch helper](#launch-helper-xbox-launch)
  - [xbWatson](#xbwatson)
  - [Debug bridge](#debug-bridge-xboxdbg-bridge)
- [Build from source](#build-from-source)

## Requirements

| Component | Requirement |
|-----------|-------------|
| Shell extension | **64-bit** Windows 10 or 11, **administrator** rights to install |
| Managed CLI tools & apps | **.NET 8 runtime** (bundled in release packages under `runtime/`, or install separately) |
| Console access | Original Xbox **development kit** on the network |

The shell extension is Windows-only. **`xbcp`**, **`xbset`**, **`xbwatson`**, and the other managed tools run on **Windows**, **Linux**, and **macOS** (x64 and Apple Silicon).

## Quick start — Xbox Neighborhood (shell extension)

1. Download or build **`XboxNeighborhood-Setup.exe`** (from [GitHub Releases](https://github.com/Team-Resurgent/RXDK-Tools/releases) or `setup/build-installer.ps1`)
2. Run the installer **as administrator** (installs the shell extension and .NET 8 Desktop Runtime if needed)
3. Open **Xbox Neighborhood** from the Start menu or desktop shortcut

The installer registers the shell extension and opens Neighborhood in Explorer. You can also navigate directly to:

```uri
shell:::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}
```

### Dev register / unregister (from source)

Build **`Rxdk.XbShellExt`** (`Release | win-x64`), then stage and register locally (scripts prompt for UAC elevation when needed):

```powershell
.\scripts\register-xbshlext-dev.ps1
.\scripts\status-xbshlext-dev.ps1
.\scripts\unregister-xbshlext-dev.ps1
```

Repo-root **`register-shell-ext.cmd`** / **`unregister-shell-ext.cmd`** are convenience wrappers. Staged payloads: **`out/dev/xbshlext/`**. Manual Explorer checks: [`docs/xbshlext-manual-checklist.md`](docs/xbshlext-manual-checklist.md).

## Quick start — Rxdk.XbNeighborhood app

Build or publish the Avalonia app (see [Rxdk.XbNeighborhood app](#rxdkxbneighborhood-app) below), then run **`xbneighborhood.exe`**. No shell extension or admin install is required.

1. **Add Console** and complete the wizard (name/IP, security if needed)
2. Select a console in the tree to browse drives
3. Double-click folders in the list to open them; use **Up** to go back

Console list persistence (via **`Rxdk.KitConfig`**):

| OS | Console list | Default console | IP cache |
|----|--------------|-----------------|----------|
| Windows | `HKCU\Software\Microsoft\XboxSDK\xbshlext\Consoles` | `HKCU\Software\Microsoft\XboxSDK\XboxName` | `HKCU\...\xbshlext\Addresses` |
| Linux / macOS | `%AppData%/Rxdk.XbNeighborhood/consoles.json` | JSON `DefaultConsole` | JSON per-console `IpAddress` |

On Windows, if the registry list is empty but `consoles.json` exists under `%AppData%\Rxdk.XbNeighborhood\`, consoles are migrated into the registry once.

**Publish (framework-dependent single-file, requires .NET 8 runtime):**

```powershell
powershell -File scripts/publish-avalonia.ps1 -Runtime win-x64
powershell -File scripts/publish-avalonia.ps1 -Runtime linux-x64
```

```bash
./scripts/publish-avalonia.sh framework linux-x64
```

Published output: `out/publish/Rxdk.XbNeighborhood-<runtime>/` (single `xbneighborhood` executable when `-r` is set).

## Screenshots

Click any image for full size.

<p align="center">
  <a href="images/neighborhood-overview.png"><img src="images/neighborhood-overview.png" width="400" alt="Xbox Neighborhood in Windows 11 Explorer"></a>
  &nbsp;
  <a href="images/console-context-menu.png"><img src="images/console-context-menu.png" width="400" alt="Context menu for a connected Xbox console"></a>
</p>
<p align="center">
  <a href="images/console-drives.png"><img src="images/console-drives.png" width="400" alt="Xbox console drives in Explorer"></a>
  &nbsp;
  <a href="images/console-audio-folder.png"><img src="images/console-audio-folder.png" width="400" alt="Browsing an Xbox console folder in Explorer"></a>
</p>

## Tools

Most CLI tools accept **`/x console`** to target a named kit. Without `/x`, they use the **default console** (see [`xbset`](#default-console-xbset)). Xbox paths use the **`xE:\`**, **`xD:\`**, … prefix (for example `xE:\title\default.xbe`).

### Xbox Neighborhood (`Rxdk.XbShellExt`)

The headline feature — an **Xbox Neighborhood** entry in Windows Explorer for browsing kits on the network with familiar folder UI.

| Feature | Description |
|---------|-------------|
| Console management | Add kits via setup wizard, set default console, view name/IP columns |
| Volume browsing | Explore `C:`, `E:`, `T:`, `U:`, and other Xbox drives |
| File operations | Cut, copy, paste, delete, rename, drag-and-drop between PC and kit |
| XBE launch | Right-click an `.xbe` on the kit and choose **Launch** |
| Reboot | Warm, cold, or same-title reboot from the console context menu |
| Capture & security | Screenshot capture and security settings from the console menu |

Built as **`Rxdk.XbShellExt.comhost.dll`** with shared **`Rxdk.Xbdm.KitServices`** / **`Rxdk.XbNeighborhood.Core`** logic and WinForms UI in-process. Run **`setup/build-installer.ps1`** (or build from **`RXDKTools.sln`**) to produce **`XboxNeighborhood-Setup.exe`**.

### Rxdk.XbNeighborhood app

Modern **Avalonia** standalone browser — browse kits, drives, and folders **without Explorer shell integration**.

| Feature | Description |
|---------|-------------|
| Console management | Add Xbox wizard, remove, set default, reboot, screenshot |
| Browse | Tree of consoles, drives, and folders; file list for contents |
| File operations | Cut, copy, paste, delete, rename, new folder, drag-and-drop |
| Copy to PC | Export to PC, or drag files/folders to Explorer |
| XBE launch | Launch `.xbe` files on the kit |
| Property pages | Console, drive, and file/folder properties |
| Security | Lock/unlock, users, permissions, admin password |

**Explorer-only** (requires shell extension registration): namespace in This PC, details pane columns, desktop shortcuts.

**Build and run:**

```powershell
dotnet run --project src/Rxdk.XbNeighborhood/Rxdk.XbNeighborhood.csproj -c Release

# Publish distributable (single-file when -r is set)
powershell -File scripts/publish-avalonia.ps1 -Runtime win-x64
```

| Component | Location |
|-----------|----------|
| Avalonia app | `src/Rxdk.XbNeighborhood/` |
| C# core logic | `src/Rxdk.XbNeighborhood.Core/` |
| Managed XBDM client | `src/Rxdk.Xbdm.Managed/` |
| Shared kit config | `src/Rxdk.KitConfig/` |

### Managed CLI tools (cross-platform)

CI publishes **framework-dependent single-file** executables (one file per tool, **.NET 8 runtime required** — not bundled into the exe). Each release zip includes:

```
out/publish/managed/<runtime>/
  tools/                         ← xbset, xbcp, xbdir, …
  runtime/                       ← offline .NET 8 runtime installer
  install-dotnet-runtime.cmd     ← Windows
  install-dotnet-runtime.sh      ← Linux / macOS
```

**First-time setup** (if .NET 8 is not already installed):

```powershell
# Windows — from extracted release zip
.\install-dotnet-runtime.cmd

# Linux / macOS
chmod +x install-dotnet-runtime.sh
./install-dotnet-runtime.sh
```

**Supported runtimes:** `win-x64`, `linux-x64`, `osx-x64` (Intel Mac), `osx-arm64` (Apple Silicon). `linux-arm64` can be built locally via `stage-managed-tools-package`.

**Build a full package locally:**

```powershell
powershell -File scripts/stage-managed-tools-package.ps1 -Runtime win-x64
```

```bash
./scripts/stage-managed-tools-package.sh linux-x64
./scripts/stage-managed-tools-package.sh osx-arm64
./scripts/stage-managed-tools-package.sh osx-x64
```

Tools-only publish (no bundled runtime):

```powershell
powershell -File scripts/publish-managed-cli-tools.ps1 -Runtime win-x64
```

```bash
./scripts/publish-managed-cli-tools.sh linux-x64
```

### Default console (`xbset`)

CLI tools (`xbcp`, `xbdir`, `xbecopy`, …) resolve the target kit in this order:

1. **`/x` / `-x`** argument
2. **`XBOXIP`** environment variable
3. **Default console** from `Rxdk.KitConfig` (`HKCU\Software\Microsoft\XboxSDK\XboxName` on Windows)

Use **`xbset`** to register a kit and set it as the default:

```cmd
xbset mykit
xbset 192.168.1.184
```

This updates the same registry keys used by Xbox Neighborhood and VS Code deploy scripts that call `xbcp` without `/x`.

### Rxdk.XboxDbgBridge (NuGet)

Tool-only **`Rxdk.XboxDbgBridge`** NuGet package: framework-dependent single-file **`xboxdbg-bridge`** binaries for **win-x64**, **linux-x64**, **osx-x64**, and **osx-arm64** under `tools/<rid>/`. No library reference — spawn the executable. Requires the **.NET 8 runtime**. PDB/stack/locals require **Windows**; kit control is cross-platform.

```bash
dotnet pack src/Rxdk.XboxDbgBridge/Rxdk.XboxDbgBridge.csproj -c Release -o out/publish/nuget
```

After install, use `tools/<rid>/xboxdbg-bridge` or MSBuild `$(RxdkXboxDbgBridgeExe)` from `build/Rxdk.XboxDbgBridge.props`.

### File utilities (`xbcp`, `xbdir`, `xbmkdir`, `xbecopy`)

Classic XDK command-line tools for moving data between the PC and a kit. Shared path parsing and connection helpers live in **`Rxdk.XbFile`**.

| Tool | Purpose |
|------|---------|
| **`xbcp`** | Copy files to or from the kit (`/r` recursive, `/s` subdirs, `/d` copy-if-newer, `/y` no prompt, `/t` create dest dir, …) |
| **`xbdir`** | List files on PC or kit (`/r` recursive, `/b` bare names, `/w` columns, `/o` sort) |
| **`xbmkdir`** | Create a directory on the kit (`/t` creates parent path as needed) |
| **`xbecopy`** | Deploy a built image to a remote Xbox path — for VS post-build steps (`local.exe` → `xe:\path\title.xbe`) |

```cmd
xbcp /r /t mybuild\* xE:\title\
xbdir /r xE:\title
xbmkdir /t xE:\data\savegames
xbecopy Debug\game.xbe E:\title\game.xbe
```

### Xbox Image File Builder (`imagebld`)

Converts a Win32 **PE** executable into a signed **`.xbe`** title image. Implementation: `src/Rxdk.ImageBld/` (`Rxdk.XbeImage` library).

| Switch | Purpose |
|--------|---------|
| **`/IN:`** / **`/OUT:`** | Input PE and output XBE paths |
| **`/STACK:`**, **`/INITFLAGS:`**, **`/INSERTFILE:`** | Image layout and embedded sections |
| **`/TITLEIMAGE:`**, **`/TITLEINFO:`**, **`/DEFAULTSAVEIMAGE:`** | Title metadata and save thumbnails |
| **`/TEST*`** | Title ID, name, region, ratings, signature key, certification fields |
| **`/DUMP`** | Inspect an existing `.xbe` and print headers/sections |

### Launch helper (`xbox-launch`)

Command-line **debug launches** for scripts or CI. Reboots the kit to pending exec (if needed), sets the title path, arms an initial breakpoint, and runs until the title stops at entry.

```cmd
xbox-launch /dir xe:\path /title game.xbe [/cmd args] [/x console] [/reboot] [/timeout ms]
```

### xbWatson

GUI **break-notification** tool from the classic XDK. Cross-platform Avalonia port at `src/Rxdk.XbWatson/`.

| Event | Behavior |
|-------|----------|
| Debug output | `DM_DEBUGSTR` text from the title |
| Asserts | Dialog with continue/ignore options |
| RIPs | Fatal error dialog with reboot choices |
| Breakpoints & exceptions | Break/exception handlers with register/context display |

```cmd
xbwatson [/x xboxname]
```

Published as **`xbwatson`** (lowercase) in the managed tools bundle.

### Debug bridge (`xboxdbg-bridge`)

**JSON-over-stdio** debug host for modern editors and automation.

```text
launch, attach, go, goUser, stop, step, waitBreak, setBreakpoint, resolveLine,
getMemory, getThreads, getStack, getVariables, evaluate, loadSymbols, shutdown
```

```powershell
echo '{"id":1,"cmd":"ping"}' | xboxdbg-bridge.exe
```

## Build from source

| Component | Requirement |
|-----------|-------------|
| Shell extension | **Visual Studio 2022** with **Desktop development with C++** (v145 toolset) |
| Managed projects | **.NET 8 SDK** |

1. Open **`RXDKTools.sln`**
2. Build **`Release | x64`**

| Output | Location |
|--------|----------|
| Shell extension + managed DLLs | `src/Rxdk.XbShellExt/bin/Release/net8.0-windows/win-x64/` |
| Native shell proxy | `out/bin/x64/Release/Rxdk.XbShellExt.Shell.dll` |
| Neighborhood installer | `out/bin/x64/Release/XboxNeighborhood-Setup.exe` (`setup/build-installer.ps1`) |
| Managed tool packages | `out/publish/managed/<runtime>/` (`scripts/stage-managed-tools-package.*`) |

### Solution layout

Managed projects live under **`src/`**. The native **`Rxdk.XbShellExt.Shell`** C++ proxy lives alongside them. Shared C/C++ headers are under **`shared/`**.

| Project | Output | Role |
|---------|--------|------|
| `Rxdk.XbShellExt` | `Rxdk.XbShellExt.comhost.dll` | Managed shell extension |
| `Rxdk.XbShellExt.Shell` | `Rxdk.XbShellExt.Shell.dll` | Native namespace proxy |
| `Rxdk.XbNeighborhood` | `xbneighborhood.exe` | Standalone Avalonia browser |
| `Rxdk.XbSet` | `xbset` | Set default console |
| `Rxdk.XbCp`, `Rxdk.XbDir`, `Rxdk.XbMkdir`, `Rxdk.XbeCopy` | `xbcp`, `xbdir`, … | File transfer utilities |
| `Rxdk.ImageBld` | `imagebld` | PE → XBE image builder |
| `Rxdk.XboxLaunch.Cli` | `xbox-launch` | CLI debug launch helper |
| `Rxdk.XbWatson` | `xbwatson` | Break/assert/RIP notification UI |
| `Rxdk.XboxDbgBridge.Cli` | `xboxdbg-bridge` | JSON debug bridge |

All managed CLI tools and apps import **`src/SingleFilePublish.props`** — publishing with `-r <rid>` produces a framework-dependent single-file executable.
