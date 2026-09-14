using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Rxdk.MsBuild.Tasks
{
    public class ZigLd : ZigToolTask
    {
        public ZigLd()
        {
            switchOrderList.AddRange(new string[] {
                "OutputFile",
                "AlwaysAppend",
                "ShowProgress",
                "Version",
                "VerboseOutput",
                "Trace",
                "TraceSymbols",
                "EntryPointSymbol",
                "PrintMap",
                "LinkerScript",
                "UnresolvedSymbolReferences",
                "OptimizeforMemory",
                "LibraryPath",
                "IgnoreSpecificDefaultLibraries",
                "ForceUndefineSymbolReferences",
                "DebuggerSymbolInformation",
                "GenerateMapFile",
                "Relocation",
                "FunctionBinding",
                "NoExecStackRequired",
                "WholeArchiveBegin",
                "AdditionalOptions",
                "Sources",
                "AdditionalDependencies",
                "WholeArchiveEnd",
                "LibraryDependencies",
            });

            errorListRegexList.Add(ldMessageRegex);
        }

        public override string SubTool => "cc";
        protected override string AlwaysAppend => JoinSwitches(new string[] {
            "-nostdlib", "-nostartfiles",
            "-Wl,--image-base=0x10000",
        });

        public virtual string OutputFile
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.File)
                    {
                        DisplayName = "Output File",
                        Description = "The option overrides the default name and location of the program that the linker creates. (-o)",
                        SwitchValue = "-o ",
                    },
                    value
                );
            }
        }

        public virtual bool ShowProgress
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Show Progress",
                        Description = "Prints Linker Progress Messages.",
                        SwitchValue = "-Wl,--stats",
                    },
                    value
                );
            }
        }

        public virtual bool Version
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Version",
                        Description = "The -version option tells the linker to put a version number in the header of the executable.",
                        SwitchValue = "-Wl,--version",
                    },
                    value
                );
            }
        }

        public virtual bool VerboseOutput
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Enable Verbose Output",
                        Description = "The -verbose option tells the linker to output verbose messages for debugging.",
                        SwitchValue = "--verbose",
                    },
                    value
                );
            }
        }

        public virtual bool Trace
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Trace",
                        Description = "The --trace option tells the linker to output the input files as are processed.",
                        SwitchValue = "-Wl,--trace",
                    },
                    value
                );
            }
        }

        public virtual string[] TraceSymbols
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        DisplayName = "Trace Symbols",
                        Description = "Print the list of files in which a symbol appears. (--trace-symbol=symbol)",
                        SwitchValue = "-Wl,--trace-symbol=",
                    },
                    value
                );
            }
        }

        public virtual string EntryPointSymbol
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Entry Point",
                        Description = "Specify an entry point function as the starting address for the title. (--entry=symbol)",
                        SwitchValue = "-Wl,--entry=",
                    },
                    value
                );
            }
        }

        public virtual bool PrintMap
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Print Map",
                        Description = "The --print-map option tells the linker to output a link map.",
                        SwitchValue = "-Wl,--print-map",
                    },
                    value
                );
            }
        }

        public virtual string LinkerScript
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Linker Script",
                        Description = "The -T option tells the linker to use a linker script.",
                        SwitchValue = "-T ",
                    },
                    value
                );
            }
        }

        public virtual bool UnresolvedSymbolReferences
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Report Unresolved Symbol References",
                        Description = "This option when enabled will report unresolved symbol references.",
                        SwitchValue = "-Wl,--no-undefined",
                    },
                    value
                );
            }
        }

        public virtual string[] LibraryPath
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringPathArray)
                    {
                        DisplayName = "Library Search Path",
                        Description = "Library search path. (-L folder).",
                        SwitchValue = "-L ",
                    },
                    value
                );
            }
        }

        public virtual bool OptimizeforMemory
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Optimize For Memory Usage",
                        Description = "Optimize for memory usage, by rereading the symbol tables as necessary.",
                        SwitchValue = "-Wl,--no-keep-memory",
                    },
                    value
                );
            }
        }

        public virtual string[] AdditionalLibraryDirectories
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringPathArray)
                    {
                        DisplayName = "Additional Library Directories",
                        Description = "Allows the user to override the environmental library path. (-L folder).",
                        SwitchValue = "-L ",
                    },
                    value
                );
            }
        }

        public virtual string[] IgnoreSpecificDefaultLibraries
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        DisplayName = "Ignore Specific Default Libraries",
                        Description = "Specifies one or more names of default libraries to ignore.",
                        SwitchValue = "-Wl,--exclude-libs=",
                    },
                    value
                );
            }
        }

        public virtual bool ForceUndefineSymbolReferences
        {
            get => PropertyOrNull<bool>(); set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Force Symbol References",
                        Description = "Force symbol to be entered in the output file as an undefined symbol.",
                        SwitchValue = "-Wl,-u--undefined=",
                    },
                    value
                );
            }
        }

        public virtual string DebuggerSymbolInformation
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Debugger Symbol Information",
                        Description = "Debugger symbol information from the output file.",
                    },
                    new Dictionary<string, string>
                    {
                        { "true", "" },
                        { "false", "" },
                        { "IncludeAll", "" },
                        { "OmitDebuggerSymbolInformation", "-Wl,--strip-debug" },
                        { "OmitAllSymbolInformation", "-Wl,--strip-all" },
                    },
                    value
                );
            }
        }

        public virtual string GenerateMapFile
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Map File Name",
                        Description = "The Map option tells the linker to create a map file with the user specified name.",
                        SwitchValue = "-Wl,-Map=",
                    },
                    value
                );
            }
        }

        public virtual bool Relocation
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Mark Variables ReadOnly After Relocation",
                        Description = "This option marks variables read-only after relocation.",
                        SwitchValue = "-Wl,-z,relro",
                        ReverseSwitchValue = "-Wl,-z,norelro",
                    },
                    value
                );
            }
        }

        public virtual bool FunctionBinding
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Enable Immediate Function Binding",
                        Description = "This option marks object for immediate function binding.",
                        SwitchValue = "-Wl,-z,now",
                    },
                    value
                );
            }
        }

        public virtual bool NoExecStackRequired
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Executable Stack Not Required",
                        Description = "This option marks output as not requiring executable stack.",
                        SwitchValue = "-Wl,-z,noexecstack",
                    },
                    value
                );
            }
        }

        public virtual bool WholeArchiveBegin
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Whole Archive",
                        Description = "Whole Archive uses all code from Sources and Additional Dependencies.",
                        SwitchValue = "-Wl,--whole-archive",
                    },
                    value
                );
            }
        }

        public virtual string[] AdditionalDependencies
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        DisplayName = "Additional Dependencies",
                        Description = "Specifies additional items to add to the link command line.",
                    },
                    value
                );
            }
        }

        public virtual bool WholeArchiveEnd
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        SwitchValue = "-Wl,--no-whole-archive",
                    },
                    value
                );
            }
        }

        public virtual string[] LibraryDependencies
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        DisplayName = "Library Dependencies",
                        Description = "This option allows specifying additional libraries to be  added to the linker command line. The additional library will be added to the end of the linker command line  with the '.lib' extension.  (-lNAME)",
                        SwitchValue = "-l",
                    },
                    value
                );
            }
        }

        protected override string GenerateResponseFileCommandsExceptSwitches(string[] switchesToRemove,
                                                                             CommandLineFormat format = CommandLineFormat.ForBuildLog,
                                                                             EscapeFormat escapeFormat = EscapeFormat.EscapeTrailingSlash)
        {
            string text = base.GenerateResponseFileCommandsExceptSwitches(switchesToRemove, format, EscapeFormat.EscapeTrailingSlash);
            text = FindBackSlashInPath.Replace(text, "\\\\");
            return text;
        }

        // Token: 0x060003C0 RID: 960 RVA: 0x0000FFC0 File Offset: 0x0000E1C0
        protected override void GenerateCommandsAccordingToType(CommandLineBuilder builder,
                                                                ToolSwitch toolSwitch,
                                                                CommandLineFormat format = CommandLineFormat.ForBuildLog,
                                                                EscapeFormat escapeFormat = EscapeFormat.Default)
        {
            if (toolSwitch.Name.Equals("AdditionalDependencies", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var value in toolSwitch.StringList)
                {
                    builder.AppendSwitchUnquotedIfNotNull("", value);
                }
                return;
            }
            base.GenerateCommandsAccordingToType(builder, toolSwitch, format, escapeFormat);
        }

        protected override void PrintMessage(MessageStruct messageStruct, MessageImportance messageImportance)
        {
            if (messageStruct != null)
            {
                if (messageStruct.Category == "")
                {
                    messageStruct.Category = "error";
                }

                if (string.Compare(messageStruct.Category, "error", StringComparison.OrdinalIgnoreCase) == 0 ||
                    string.Compare(messageStruct.Category, "fatal error", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    errorCount++;
                }
            }

            base.PrintMessage(messageStruct, messageImportance);
        }

        protected static Regex ldMessageRegex = new Regex("^\\s*(?<FILENAME>[^:]*):(((?<LINE>\\d*):)?)(\\s*(?<CATEGORY>(fatal error|error|warning|note)):)?\\s*(?<TEXT>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100.0));
        private int errorCount = 0;
    }
}
