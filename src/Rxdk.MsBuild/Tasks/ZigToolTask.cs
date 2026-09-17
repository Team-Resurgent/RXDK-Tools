using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
//using Rxdk.Engine.Bootstrap;
//using Rxdk.Engine.Platform;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rxdk.MsBuild.Tasks
{
    public abstract class ZigToolTask : RxdkToolTask
    {
        public ZigToolTask()
        {
            switchOrderList = new ArrayList()
            {
                "Target",
                "Machine"
            };
        }

        protected override string GenerateFullPathToTool()
        {
            return ToolName;
        }

        // Pinned Zig version the SDK is built/tested against (mirrors Rxdk.Engine ZigRuntime.ZigVersion).
        private const string ZigVersion = "0.16.0";

        protected override string ToolName
        {
            get
            {
                // RXDK_ZIG is only an OVERRIDE (CI, a non-standard install). The normal case is a
                // bare VSIX install whose "Complete Setup" put the pinned Zig at the managed default
                // location with no env var set -- so fall back to it exactly like Rxdk.Engine's
                // ZigRuntime does, rather than hard-failing on an unset env var.
                var zig = Environment.GetEnvironmentVariable("RXDK_ZIG");
                if (!string.IsNullOrEmpty(zig))
                {
                    zig = zig.Trim();
                    if (File.Exists(zig))
                        return zig;
                    FatalError($"RXDK_ZIG points to a missing file: {zig}");
                    return "";
                }

                foreach (var candidate in ManagedZigCandidates())
                    if (File.Exists(candidate))
                        return candidate;

                FatalError("Zig is not installed. Open the RXDK tool window and run Complete Setup (or set RXDK_ZIG).");
                return "";
            }
        }

        // The managed pinned-Zig install, Windows layout (mirrors Rxdk.Engine ZigRuntime /
        // RxdkPaths.GetZigInstallRoot): %LocalAppData%\RXDK\zig\<ver>\zig-x86_64-windows-<ver>\zig.exe.
        private static IEnumerable<string> ManagedZigCandidates()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RXDK", "zig");
            yield return Path.Combine(root, ZigVersion, $"zig-x86_64-windows-{ZigVersion}", "zig.exe");
            yield return Path.Combine(root, ZigVersion, "zig.exe");
        }
        public abstract string SubTool { get; }
        public string Target => "x86-windows-gnu";
        public string Machine => "-march=pentium3";

        // the sub tool must be on the command line or response files will not be processed
        protected override string GenerateCommandLineCommandsExceptSwitches(string[] switchesToRemove, CommandLineFormat format = CommandLineFormat.ForBuildLog, EscapeFormat escapeFormat = EscapeFormat.Default)
        {
            return SubTool;
        }

        protected override void AddDefaultsToActiveSwitchList()
        {
            UpdateSwitch(
                new ToolSwitch(ToolSwitchType.String)
                {
                    DisplayName = "Target",
                    Description = "The target triple to build for.",
                    SwitchValue = "-target ",
                },
                Target,
                "Target"
            );
            UpdateSwitch(
                new ToolSwitch(ToolSwitchType.Boolean)
                {
                    DisplayName = "Machine",
                    Description = "The machine to build for.",
                    SwitchValue = Machine,
                },
                true,
                "Machine"
            );
        }

        [Required]
        public virtual ITaskItem[] Sources
        {
            get => PropertyOrNull<ITaskItem[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.ITaskItemArray)
                    {
                        Separator = " ",
                        Required = true,
                    },
                    value
                );
            }
        }

        protected override ITaskItem[] TrackedInputFiles => Sources;
        protected override Encoding ResponseFileEncoding => Encoding.ASCII;
        protected override Encoding StandardOutputEncoding => Encoding.UTF8;
        protected override Encoding StandardErrorEncoding => Encoding.UTF8;
    }
}
