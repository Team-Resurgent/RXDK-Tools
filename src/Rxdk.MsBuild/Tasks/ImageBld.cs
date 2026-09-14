using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxdk.MsBuild.Tasks
{

    public class ImageBld : RxdkToolTask
    {
        public ImageBld()
        {
            switchOrderList = new ArrayList()
            {
                "OutputFile",
                "InputFile",
                "Dxt",
                "StackSize",
                "Debug",
                "NoLogo",
                "NoLibWarn",
                "LimitMemory",
                "DontModifyHardDisk",
                "DontMountUtilityDrive",
                "FormatUtilityDrive",
                "UtilityDriveClusterSize",
                "NoPreload",
                "TestId",
                "TestAltId",
                "TestRegion",
                "TestRatings",
                "TestMediaTypes",
                "TestLanKey",
                "TestSignKey",
                "TestName",
                "TestVersion",
                "TitleInfo",
                "TitleImage",
                "DefaultSaveImage",
            };
        }

        protected override string ToolName => "imagebld.exe";

        public virtual string OutputFile
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.File)
                    {
                        DisplayName = "Output File",
                        Description = "The option overrides the default name and location of the XBE that imagebld creates. (/out)",
                        SwitchValue = "/out:",
                    },
                    value
                );
            }
        }

        [Required]
        public virtual ITaskItem InputFile
        {
            get => PropertyOrNull<ITaskItem>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.ITaskItem)
                    {
                        SwitchValue = "/in:",
                        Required = true,
                    },
                    value
                );
            }
        }

        public bool Dxt
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Build Debugger Extension",
                        Description = "Build a debugger extension (.dxt) instead of an Xbox title (.xbe). (/dxt)",
                        SwitchValue = "/dxt"
                    },
                    value
                );
            }
        }

        public int StackSize
        {
            get => PropertyOrNull<int>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Integer)
                    {
                        DisplayName = "Stack Size",
                        Description = "Title thread stack size in bytes. (/stack)",
                        SwitchValue = "/stack:",
                        IsValid = true
                    },
                    value
                );
            }
        }

        public bool Debug
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Include Debug Info",
                        Description = "Include the debug directory in the XBE so the debugger can resolve symbols. (/debug)",
                        SwitchValue = "/debug"
                    },
                    value
                );
            }
        }

        public bool NoLogo
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Suppress Startup Banner",
                        Description = "Do not print the imagebld banner. (/nologo)",
                        SwitchValue = "/nologo"
                    },
                    value
                );
            }
        }

        public bool NoLibWarn
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Disable Library Warnings",
                        Description = "Suppress library-version warnings from imagebld. (/nolibwarn)",
                        SwitchValue = "/nolibwarn"
                    },
                    value
                );
            }
        }

        public bool LimitMemory
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Limit Memory (64 MB)",
                        Description = "Run the title as if the console has only 64 MB of RAM. (/limitmem)",
                        SwitchValue = "/limitmem"
                    },
                    value
                );
            }
        }

        public bool DontModifyHardDisk
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Don't Modify Hard Disk",
                        Description = "Prevent the title from modifying the retail hard disk layout. (/dontmodifyhd)",
                        SwitchValue = "/dontmodifyhd"
                    },
                    value
                );
            }
        }

        public bool DontMountUtilityDrive
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Don't Mount Utility Drive",
                        Description = "Do not mount the utility (Z:) drive at launch (/dontmountud).  Cannot be combined with Format Utility Drive.",
                        SwitchValue = "/dontmountud",
                    },
                    value
                );
            }
        }

        public bool FormatUtilityDrive
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Format Utility Drive",
                        Description = "Format the utility (Z:) drive at launch (/formatud).  Cannot be combined with Don't Mount Utility Drive.",
                        SwitchValue = "/formatud"
                    },
                    value
                );
            }
        }

        public string UtilityDriveClusterSize
        {
            get => PropertyOrNull<int>().ToString();
            set
            {
                int dummy = 0;
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Integer)
                    {
                        DisplayName = "Utility Drive Cluster Size",
                        Description = "Cluster size for the utility drive. (/udcluster)",
                        SwitchValue = "/udcluster:",
                        IsValid = int.TryParse(value, out dummy)
                    },
                    new Dictionary<string, string>
                    {
                        {"Default", ""},
                        {"16KB", "16384"},
                        {"32KB", "32768"},
                        {"64KB", "65536"},
                        {"128KB", "131072"},
                        {"256KB", "262144"},
                    },
                    value
                );
            }
        }

        public string[] NoPreload
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        SwitchValue = "/nopreload:"
                    },
                    value
                );
            }
        }

        public string TestId
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testid:",
                        Separator = ";",
                    },
                    value
                );
            }
        }

        public string TestAltId
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testaltid:"
                    },
                    value
                );
            }
        }

        public string TestRegion
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testregion:"
                    },
                    value
                );
            }
        }

        public string TestRatings
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testratings:"
                    },
                    value
                );
            }
        }

        public string TestMediaTypes
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testmediatypes:"
                    },
                    value
                );
            }
        }

        public string TestLanKey
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testlankey:"
                    },
                    value
                );
            }
        }

        public string TestSignKey
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testsignkey:"
                    },
                    value
                );
            }
        }

        public string TestName
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testname:"
                    },
                    value
                );
            }
        }

        public string TestVersion
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/testversion:"
                    },
                    value
                );
            }
        }

        public string TitleInfo
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/titleinfo:"
                    },
                    value
                );
            }
        }

        public string TitleImage
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/titleimage:"
                    },
                    value
                );
            }
        }

        public string DefaultSaveImage
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        SwitchValue = "/defaultsaveimage:"
                    },
                    value
                );
            }
        }

        protected override ITaskItem[] TrackedInputFiles => new ITaskItem[] { InputFile };
    }


}
