# RXDK Tools

<p align="center">
  <a href="https://discord.gg/VcdSfajQGK"><img src="https://img.shields.io/badge/chat-on%20discord-7289da.svg?logo=discord" alt="Discord"></a>
  &nbsp;
  <a href="https://ko-fi.com/J3J7L5UMN"><img src="https://ko-fi.com/img/githubbutton_sm.svg" alt="ko-fi"></a>
  &nbsp;
  <a href="https://www.patreon.com/teamresurgent"><img src="https://img.shields.io/badge/Patreon-F96854?style=for-the-badge&logo=patreon&logoColor=white" alt="Patreon"></a>
</p>

**Recompiled Original Xbox XDK host tools for 64-bit Windows 10 and Windows 11.**

The classic XDK shipped 32-bit utilities that no longer run on modern Windows. RXDK Tools rebuilds them as **x64-native** ports. All console-facing tools use a statically linked **`xbdbgs.lib`** client and talk to kits over the **Xbox Debug Monitor (XBDM)** protocol.

**What's included**

| Category | Tools |
|----------|-------|
| Explorer | **Xbox Neighborhood** shell extension (`xbshlext.dll`) |
| File transfer | `xbcp`, `xbdir`, `xbmkdir`, `xbecopy` |
| Build | `imagebld` (PE → signed `.xbe`) |
| Debug | `xbox-launch`, `xbWatson`, `xboxdbg-bridge` |

## Contents

- [Requirements](#requirements)
- [Quick start — Xbox Neighborhood](#quick-start--xbox-neighborhood)
- [Screenshots](#screenshots)
- [Tools](#tools)
  - [Xbox Neighborhood](#xbox-neighborhood-xbshlextdll)
  - [File utilities](#file-utilities-xbcp-xbdir-xbmkdir-xbecopy)
  - [Image builder](#xbox-image-file-builder-imagebld)
  - [Launch helper](#launch-helper-xbox-launch)
  - [xbWatson](#xbwatson-xbwatsonexe)
  - [Debug bridge](#debug-bridge-xboxdbg-bridgeexe)
- [Build from source](#build-from-source)

## Requirements

- **64-bit** Windows 10 or Windows 11
- An Original Xbox **development kit** on the network (for console-facing tools)
- **Administrator** rights to install the shell extension (registered machine-wide)

## Quick start — Xbox Neighborhood

1. Download or build **`XboxNeighborhood-Setup.exe`**
2. Run the installer **as administrator**
3. Open **Xbox Neighborhood** from the Start menu or desktop shortcut

The installer registers the shell extension and opens Neighborhood in Explorer. You can also navigate directly to:

```uri
shell:::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}
```

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

Binaries build to **`out/bin/x64/Release/`**. Most CLI tools accept **`/x console`** to target a named kit. Xbox paths use the **`xE:\`**, **`xD:\`**, … prefix (for example `xE:\title\default.xbe`).

### Xbox Neighborhood (`xbshlext.dll`)

The headline feature — an **Xbox Neighborhood** entry in Windows Explorer for browsing kits on the network with familiar folder UI.

| Feature | Description |
|---------|-------------|
| Console management | Add kits via setup wizard, set default console, view name/IP columns |
| Volume browsing | Explore `C:`, `E:`, `T:`, `U:`, and other Xbox drives |
| File operations | Cut, copy, paste, delete, rename, drag-and-drop between PC and kit |
| XBE launch | Right-click an `.xbe` on the kit and choose **Launch** |
| Reboot | Warm, cold, or same-title reboot from the console context menu |
| Capture & security | Screenshot capture and security settings from the console menu |

Built as `xbshlext.dll`. The Inno Setup post-build step produces **`XboxNeighborhood-Setup.exe`**.

### File utilities (`xbcp`, `xbdir`, `xbmkdir`, `xbecopy`)

Classic XDK command-line tools for moving data between the PC and a kit. Shared path parsing and connection helpers live in **`xbfile.lib`**.

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

Converts a Win32 **PE** executable into a signed **`.xbe`** title image — the same `imagebld` from the XDK build pipeline. Links **`xrsa.lib`** (built from `src/xrsa/`) instead of the old prebuilt `rsa32.lib`.

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

Subscribes to exec, break, module-load, and debug-string notifications. Useful for a deterministic “stop at main” session without opening the full Visual Studio debugger.

### xbWatson (`xbWatson.exe`)

GUI **break-notification** tool from the classic XDK. Leave it running while developing — it connects to the kit and surfaces debug events in a log window with modal dialogs for interactive cases.

| Event | Behavior |
|-------|----------|
| Debug output | `DM_DEBUGSTR` text from the title |
| Asserts | Dialog with continue/ignore options |
| RIPs | Fatal error dialog with reboot choices |
| Breakpoints & exceptions | Break/exception handlers with register/context display |

```cmd
xbWatson [/x xboxname]
```

Pairs well with Neighborhood or `xbox-launch` when you want visible feedback from `DbgPrint`, asserts, and crashes without a full debugger attach.

### Debug bridge (`xboxdbg-bridge.exe`)

**JSON-over-stdio** debug host for modern editors and automation. Reads one JSON command per line on stdin, writes JSON results/events on stdout — intended for DAP front-ends and scripted debug sessions.

Supported commands:

```text
launch, attach, go, goUser, stop, step, waitBreak, setBreakpoint, resolveLine,
getMemory, getThreads, getStack, getVariables, evaluate, loadSymbols, shutdown
```

Symbol resolution uses **`dbghelp`** (PDB/map) with Xbox address relocation.

```powershell
echo '{"id":1,"cmd":"ping"}' | xboxdbg-bridge.exe
```

## Build from source

Requires **Visual Studio 2022** with **Desktop development with C++** (v145 toolset).

1. Open **`RXDKTools.sln`**
2. Build **`Release | x64`**

| Output | Location |
|--------|----------|
| Executables & DLL | `out/bin/x64/Release/` |
| Static libraries | `out/lib/x64/Release/` (`xbdbgs.lib`, `xbfile.lib`, `xrsa.lib`) |
| Neighborhood installer | `out/bin/x64/Release/XboxNeighborhood-Setup.exe` (built with **`xbshlext`**; Inno Setup installed automatically if missing) |

### Solution layout

Each `.vcxproj` lives alongside its sources under `src/`; shared headers and helpers are under `shared/`.

| Project | Output | Role |
|---------|--------|------|
| `xbdbgs` | `xbdbgs.lib` | Static XBDM client (connection, files, notifications, debug API) |
| `xbfile` | `xbfile.lib` | Shared path/option parsing for file CLI tools |
| `xrsa` | `xrsa.lib` | Source-built RSA/crypto for `imagebld` signing |
| `xbshlext` | `xbshlext.dll` | Xbox Neighborhood shell extension |
| `xbcp`, `xbdir`, `xbmkdir`, `xbecopy` | `*.exe` | File transfer utilities |
| `imagebld` | `imagebld.exe` | PE → XBE image builder |
| `xbox-launch` | `xbox-launch.exe` | CLI debug launch helper |
| `xbWatson` | `xbWatson.exe` | Break/assert/RIP notification UI |
| `xboxdbg_bridge` | `xboxdbg-bridge.exe` | JSON debug bridge for editor integration |

All executables link **`xbdbgs.lib`** statically. Everything targets **x64**, including `imagebld` and full notification support in `xbdbgs`.
