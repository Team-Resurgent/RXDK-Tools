using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxdk.MsBuild.Tasks
{
    public class ZigAr : ZigToolTask
    {
        public ZigAr()
        {
            // dont need target or machine
            switchOrderList = new ArrayList {
                "Command",
                "AlwaysAppend",
                "CreateIndex",
                "CreateThinArchive",
                "NoWarnOnCreate",
                "TruncateTimestamp",
                "SuppressStartupBanner",
                "Verbose",
                "AdditionalOptions",
                "OutputFile",
                "Sources",
            };
        }

        public override string SubTool => "ar";
        protected override string AlwaysAppend => "-r";

        public virtual string Command
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Command",
                        Description = "Command for AR.",
                    },
                    new Dictionary<string, string>
                    {
                        { "Delete", "-d" },
                        { "Move", "-m" },
                        { "Print", "-p" },
                        { "Quick", "-q" },
                        { "Replacement", "-r" },
                        { "Table", "-t" },
                        { "Extract", "-x" },
                    },
                    value
                );
            }
        }

        public virtual bool CreateIndex
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Create an archive index",
                        Description = "Create an archive index (cf. ranlib).  This can speed up linking and reduce dependency within its own library.",
                        SwitchValue = "-s",
                    },
                    value
                );
            }
        }

        public virtual bool CreateThinArchive
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Create Thin Archive",
                        Description = "Create a thin archive.  A thin archive contains relativepaths to the objects instead of embedding the objects.  Switching between Thin and Normal requires deleting the existing library.",
                        SwitchValue = "-T",
                    },
                    value
                );
            }
        }

        public virtual bool NoWarnOnCreate
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "No Warning on Create",
                        Description = "Do not warn if when the library is created.",
                        SwitchValue = "-c",
                    },
                    value
                );
            }
        }

        public virtual bool TruncateTimestamp
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Truncate Timestamp",
                        Description = "Use zero for timestamps and uids/gids.",
                        SwitchValue = "-D",
                    },
                    value
                );
            }
        }

        public virtual bool SuppressStartupBanner
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Suppress Startup Banner",
                        Description = "Don't show version number.",
                        ReverseSwitchValue = "-V",
                    },
                    value
                );
            }
        }

        public virtual bool Verbose
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Verbose",
                        Description = "Verbose",
                        SwitchValue = "-v",
                    },
                    value
                );
            }
        }

        public virtual string OutputFile
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.File)
                    {
                        Separator = " ",
                        DisplayName = "Output File",
                        Description = "Override the default name and location of the library.",
                        Required = true,
                    },
                    value
                );
            }
        }
    }
}
