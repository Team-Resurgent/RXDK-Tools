# Rxdk.XboxDbgBridge

Cross-platform **xboxdbg-bridge** for original Xbox kit debugging (stdin/JSON protocol used by VS Code DAP and other editors).

This package ships **framework-dependent single-file executables** — no library reference is required. Spawn the binary for your OS/runtime (**.NET 8 runtime** must be installed):

| RID | Path in package |
|-----|-----------------|
| Windows x64 | `tools/win-x64/xboxdbg-bridge.exe` |
| Linux x64 | `tools/linux-x64/xboxdbg-bridge` |
| macOS arm64 | `tools/osx-arm64/xboxdbg-bridge` |

MSBuild imports `build/Rxdk.XboxDbgBridge.props`, which sets `$(RxdkXboxDbgBridgeExe)` to the matching binary.

**Symbols** (PDB line breakpoints, stack, locals, evaluate) require **Windows** (DbgHelp inside the bridge). Kit control (launch, go, stop, address breakpoints) works on all platforms.
