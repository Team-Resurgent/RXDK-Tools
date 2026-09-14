# RXDK MSBuild Integration (experimental)

This is an attempt to bring the experience of using RXDK with Visual Studio up to the same level as
official console SDKs offer. I wrote up some [notes](NOTES.md) about how it works.

It's currently experimental, but it's able to build a proper XBE.

## Usage

For now, this isn't included in the VSIX. To use it, copy the `RXDK` folder to here:
```
C:\Program Files\Microsoft Visual Studio\<your VS version>\Enterprise\MSBuild\Microsoft\VC\v170\Application Type
```

Then set the following environment variables:
- `RXDK` to `C:\ProgramData\RXDK` or wherever you installed RXDK
- `RXDK_ZIG` to the Zig executable that RXDK installed, i.e. `%LOCALAPPDATA%\RXDK\zig\zig\0.16.0\zig-x86_64-windows-0.16.0\zig.exe`

Here are some notes if you're planning to work on it:
- Any modifications to the files won't be picked up by Visual Studio until you restart it
- Unless you're working on property pages, using MSBuild from the command line lets you iterate faster
- Symlinking the `RXDK` folder into `Application Types` is super handy
