using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
//using Rxdk.Engine.Bootstrap;
//using Rxdk.Engine.Platform;
using System;
using System.Collections;
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

        protected override string ToolName
        {
            get
            {
                var zig = Environment.GetEnvironmentVariable("RXDK_ZIG");
                if (string.IsNullOrEmpty(zig))
                {
                    FatalError("The RXDK_ZIG environment variable is not set, did you install correctly?");
                    return "";
                }

                return zig;
            }
        }
        //ZigRuntime.ResolveZigExecutableAsync().GetAwaiter().GetResult() ??
        //  throw new FileNotFoundException("Zig not found.");
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
