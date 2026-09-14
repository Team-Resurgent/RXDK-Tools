using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Rxdk.MsBuild.Tasks
{
    public class ZigCompile : ZigToolTask
    {
        public ZigCompile()
        {
            switchOrderList.AddRange(new string[] {
                "Optimization",
                "AlwaysAppend",
                "DebugInfo",
                "AdditionalIncludeDirectories",
                "ObjectFileName",
                "WarningLevel",
                "TreatWarningAsError",
                "Verbose",
                "TrackerLogDirectory",
                "StrictAliasing",
                "OmitFramePointers",
                "FunctionLevelLinking",
                "DataLevelLinking",
                //"BufferSecurityCheck",
                "RuntimeTypeInfo",
                "CLanguageStandard",
                "CppLanguageStandard",
                "PreprocessorDefinitions",
                "UndefinePreprocessorDefinitions",
                "UndefineAllPreprocessorDefinitions",
                "PrecompiledHeader",
                "PrecompiledHeaderFile",
                "PrecompiledHeaderOutputFileDirectory",
                "PrecompiledHeaderCompileAs",
                "CompileAs",
                "ForcedIncludeFiles",
                "AdditionalOptions",
                "Sources",
            });
        }

        public override string SubTool => "cc";

        public virtual string Optimization
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Optimization",
                        Description = "Specifies the optimization level for the title.",
                        Value = value,
                        MultipleValues = true,
                    },
                    new Dictionary<string, string> {
                        {"None", "-O0"},
                        {"FavorSpeed", "-O2"},
                        {"FavorSize", "-Os"},
                        {"Full", "-O3"},
                    },
                    value
                );
            }
        }

        protected override string AlwaysAppend => JoinSwitches(new string[] {
            // want to just compile files right now
            "-c",

            "-ffreestanding", "-fno-stack-protector", "-fms-extensions", "-fms-compatibility",
            "-fms-compatibility-version=19.44",
            "-nostdinc", "-march=pentium3",
            // Every Xbox title is built with _XBOX/XBOX defined (the XDK did this); a lot of
            // Xbox headers/code select their platform path on it.
            "-D_XBOX", "-DXBOX",
            // Keep Clang from inline-expanding memmove/memcpy-shaped calls past picolibc's
            // -fno-builtin implementations, and pin the retail (_DEBUG-off) SDK link path.
            "-fno-builtin", // "-U_DEBUG", // hopefully _DEBUG can stay working
            // picolibc's default assert() calls __assert_no_args(), which prints a bare
            // "assertion failed" -- useless for locating a fault in a title. Ask for the
            // variant that reports the expression, file and line.
            "-D__ASSERT_VERBOSE",
            // Thread-local storage: emulated TLS (a per-thread table reached via
            // __emutls_get_address, backed by libc tss/emutls.c) instead of the native
            // Windows __tls_index/TEB %fs model, which the RXDK runtime never sets up.
            // Without this, any title `__thread`/`thread_local` (e.g. stb_image's
            // stbi__g_failure_reason / vertically_flip_on_load) reads a wild fixed
            // address and bugchecks. Matches how libcpp is built (xbox_target.zig
            // cppFlags).
            "-femulated-tls",

            "-fno-sanitize=undefined",

            // -I (not -isystem) everywhere: the SDK's clean-room windef.h/etc. must win over zig's
            // bundled MinGW headers, which -isystem would let shadow them.
            // The sample + framework code is compiled warning-clean; only these unavoidable suppressions
            // remain, and none of them is a fixable source defect:
            //   * c++11-narrowing / address-of-temporary — clang treats these as hard ERRORS on legacy
            //     XDK idioms (braced-init narrowing e.g. STRING={(USHORT)strlen(s),...}; and taking the
            //     address of a temporary passed to a D3DX helper, D3DXVec3Cross(&out,&D3DXVECTOR3(...),...)
            //     — the temporary lives to end-of-expression so the callee is safe). Rewriting Microsoft's
            //     reference idioms is out of scope.
            //   * ignored-pragma-intrinsic — clang cannot honor MSVC's `#pragma intrinsic`; harmless.
            //   * multichar — the XDK FOURCC idiom ('YV12' etc.) is intentional, not a bug.
            //   * unused-command-line-argument — build-driver noise (a flag that doesn't apply to a TU).
            //   * deprecated-enum-enum-conversion — the D3D8 pixel-shader register-combiner API is
            //     *defined* by OR-ing the named PS_REGISTER / PS_CHANNEL / PS_INPUTMAPPING enums together
            //     (see d3d8types.h's own combiner examples). It is the documented, retail-faithful idiom;
            //     C++20 deprecates cross-enum bitwise ops in general but this usage is correct by design,
            //     and casting at every combiner call site across the shader samples would only obscure it.
            "-Wno-c++11-narrowing",
            "-Wno-address-of-temporary",
            "-Wno-ignored-pragma-intrinsic",
            "-Wno-multichar",
            "-Wno-unused-command-line-argument",
            "-Wno-deprecated-enum-enum-conversion",
        });

        public virtual string DebugInfo
        {
            get => PropertyOrNull<string>();
            set => UpdateSwitch(
                new ToolSwitch(ToolSwitchType.String)
                {
                    DisplayName = "Debug Information",
                    Description = "Specifies the type of debugging information generated by the compiler.",
                    MultipleValues = true
                },
                new Dictionary<string, string>
                {
                    {"None", ""},
                    {"LineTables", "-gline-tables-only"},
                    {"Full", "-g"},
                },
                value
            );
        }

        public virtual string[] AdditionalIncludeDirectories
        {
            get => PropertyOrNull<string[]>();
            set => UpdateSwitch(
                new ToolSwitch(ToolSwitchType.StringPathArray)
                {
                    DisplayName = "Additional Include Directories",
                    Description = "Specifies one or more directories to add to the include path; separate with semi-colons if more than one. (-I[path]).",
                    SwitchValue = "-I ",
                },
                value
            );
        }

        public virtual string ObjectFileName
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Object File Name",
                        Description = "Specifies a name to override the default object file name; can be file or directory name. (-o [name]).",
                        SwitchValue = "-o ",
                    },
                    value);
            }
        }

        public virtual string WarningLevel
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Warning Level",
                        Description = "Select how strict you want the compiler to be about code errors.  Other flags should be added directly to Additional Options. (-w, -Wall).",
                    },
                    new Dictionary<string, string>
                    {
                        {"TurnOffAllWarnings", "-w"},
                        {"EnableDefaultWarnings", ""},
                        {"EnableAllWarnings", "-Wall"},
                        {"EnableExtraWarnings", "-Wall -Wextra"},
                    },
                    value
                );
            }
        }

        public virtual bool TreatWarningAsError
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Treat Warnings As Errors",
                        Description = "Treats all compiler warnings as errors. For a new project, it may be best to use -Werror in all compilations; resolving all warnings will ensure the fewest possible hard-to-find code defects.",
                        SwitchValue = "-Werror",
                    },
                    value
                );
            }
        }

        public virtual string[] DisableSpecificWarnings
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        DisplayName = "Disable Specific Warnings",
                        Description = "Disable specified compiler warnings. (-Wno-[name])",
                        SwitchValue = "-Wno-",
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
                        DisplayName = "Enable Verbose mode",
                        Description = "Show commands to run and use verbose output.",
                        SwitchValue = "-v",
                    },
                    value
                );
            }
        }

        //protected bool StrictAliasing
        //{
        //    get => PropertyOrNull<bool>("StrictAliasing");
        //    set
        //    {
        //        UpdateSwitch(
        //            "StrictAliasing",
        //            new ToolSwitch(ToolSwitchType.Boolean)
        //            {
        //                DisplayName = "Strict Aliasing",
        //                Description = "Assume the strictest aliasing rules.  An object of one type will never be assumed to reside at the same address as an object of a different type.",
        //                SwitchValue = "-fstrict-aliasing",
        //                ReverseSwitchValue = "-fno-strict-aliasing",
        //            },
        //            value
        //        );
        //    }
        //
        //}

        public virtual bool OmitFramePointers
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Omit Frame Pointer",
                        Description = "Suppresses creation of frame pointers on the call stack.",
                        SwitchValue = "-fomit-frame-pointer",
                        ReverseSwitchValue = "-fno-omit-frame-pointer",
                    },
                    value
                );
            }
        }

        public virtual bool FunctionLevelLinking
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Enable Function-Level Linking",
                        Description = "Allows the compiler to package individual functions in the form of packaged functions (COMDATs). Required for edit and continue to work.     (ffunction-sections).",
                        SwitchValue = "-ffunction-sections",
                    },
                    value
                );
            }
        }

        public virtual bool DataLevelLinking
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Enable Data-Level Linking",
                        Description = "Enables linker optimizations to remove unused data by emitting each data item in a separate section.",
                        SwitchValue = "-fdata-sections",
                    },
                    value
                );
            }
        }

        public virtual bool BufferSecurityCheck
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Security Check",
                        Description = "The Security Check helps detect stack-buffer over-runs, a common attempted attack upon a program's security. (fstack-protector).",
                        SwitchValue = "-fstack-protector"
                    },
                    value
                );
            }
        }

        public virtual bool RuntimeTypeInfo
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Enable Run-Time Type Information",
                        Description = "Adds code for checking C++ object types at run time (runtime type information).     (frtti, fno-rtti)",
                        SwitchValue = "-frtti",
                        ReverseSwitchValue = "-fno-rtti",
                    },
                    value
                );
            }
        }

        public virtual string CLanguageStandard
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "C Language Standard",
                        Description = "Determines the C language standard.",
                    },
                    new Dictionary<string, string>
                    {
                        { "Default", "" },
                        { "c89", "-std=c89" },
                        { "iso9899:199409", "-std=iso9899:199409" },
                        { "gnu89", "-std=gnu89" },
                        { "c99", "-std=c99" },
                        { "gnu99", "-std=gnu99" },
                        { "c11", "-std=c11" },
                        { "gnu11", "-std=gnu11" },
                        { "c17", "-std=c17" },
                        { "gnu17", "-std=gnu17" },
                        { "c23", "-std=c23" },
                        { "gnu23", "-std=gnu23" },
                        { "c2y", "-std=c2y" },
                        { "gnu2y", "-std=gnu2y" }
                    },
                    value
                );
            }
        }

        public virtual string CppLanguageStandard
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "C++ Language Standard",
                        Description = "Determines the C++ language standard.",
                    },
                    new Dictionary<string, string>
                    {
                        { "Default", "" },
                        { "c++98", "-std=c++98" },
                        { "gnu++98", "-std=gnu++98" },
                        { "c++11", "-std=c++11" },
                        { "gnu++11", "-std=gnu++11" },
                        { "c++14", "-std=c++14" },
                        { "gnu++14", "-std=gnu++14" },
                        { "c++17", "-std=c++17" },
                        { "gnu++17", "-std=gnu++17" },
                        { "c++20", "-std=c++20" },
                        { "gnu++20", "-std=gnu++20" },
                        { "c++23", "-std=c++23" },
                        { "gnu++23", "-std=gnu++23" },
                        { "c++2c", "-std=c++2c" },
                        { "gnu++2c", "-std=gnu++2c" },
                        { "c++2d", "-std=c++2d" },
                        { "gnu++2d", "-std=gnu++2d" },
                    },
                    value
                );
            }
        }

        public virtual string[] PreprocessorDefinitions
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        DisplayName = "Preprocessor Definitions",
                        Description = "Defines a preprocessing symbols for your source file. (-D)",
                        SwitchValue = "-D ",
                    },
                    value
                );
            }
        }

        public virtual string[] UndefinePreprocessorDefinitions
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        DisplayName = "Undefine Preprocessor Definitions",
                        Description = "Specifies one or more preprocessor undefines.  (-U [macro])",
                        SwitchValue = "-U ",
                    },
                    value);
            }
        }

        public virtual bool UndefineAllPreprocessorDefinitions
        {
            get => PropertyOrNull<bool>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Boolean)
                    {
                        DisplayName = "Undefine All Preprocessor Definitions",
                        Description = "Undefine all previously defined preprocessor values.  (-undef)",
                        SwitchValue = "-undef",
                    },
                    value
                );
            }
        }

        public virtual string PrecompiledHeader
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Precompiled Header",
                        Description = "Create/Use Precompiled Header:Enables creation or use of a precompiled header during the build.",
                    },
                    new Dictionary<string, string> {
                        { "Create", "" },
                        { "Use", "" },
                        { "NotUsing", "" },
                    },
                    value
                );
            }
        }

        public virtual string PrecompiledHeaderFile
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.File)
                    {
                        DisplayName = "Precompiled Header File",
                        Description = "Specifies header file name to use for precompiled header file. This file will be also added to 'Forced Include Files' during build",
                    },
                    value
                );
            }
        }

        public virtual string PrecompiledHeaderOutputFileDirectory
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.Directory)
                    {
                        DisplayName = "Precompiled Header Output File Directory",
                        Description = "Specifies the directory for the generated precompiled header. This directory will be also added to 'Additional Include Directories' during build",
                    },
                    value
                );
            }
        }

        public virtual string PrecompiledHeaderCompileAs
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Compile Precompiled Header As",
                        Description = "Select compile language option for precompiled header file (-x c-header, -x c++-header).",
                    },
                    new Dictionary<string, string>
                    {
                        { "CompileAsC", "-x c-header" },
                        { "CompileAsCpp", "-x c++-header" },
                    },
                    value
                );
            }
        }

        public virtual string CompileAs
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.String)
                    {
                        DisplayName = "Compile As",
                        Description = "Select compile language option for .c and .cpp files.  'Default' will detect based on .c or .cpp extention. (-x c, -x c++)",
                    },
                    new Dictionary<string, string>
                    {
                        { "Default", "" },
                        { "CompileAsC", "-x c" },
                        { "CompileAsCpp", "-x c++" },
                        { "CompileAsAsm", "-x assembler-with-cpp" },
                    },
                    value
                );
            }
        }

        public virtual string[] ForcedIncludeFiles
        {
            get => PropertyOrNull<string[]>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.StringArray)
                    {
                        DisplayName = "Forced Include Files",
                        Description = "one or more forced include files.     (-include [name])",
                        SwitchValue = "-include ",
                    },
                    value
                );
            }
        }

        //private string firstReadTLog { get => $"{typeof(ZigCompile).FullName}.read.1.tlog"; }
        //protected override string[] ReadTLogNames
        //{
        //    get => new string[] {
        //            firstReadTLog,
        //            $"{SubTool}.read.*.tlog",
        //            $"{SubTool}.*.read.*.tlog",
        //            $"{SubTool}-*.read.*.tlog",
        //            $"{SubTool}.delete.*.tlog",
        //            $"{SubTool}.*.delete.*.tlog",
        //            $"{SubTool}-*.delete.*.tlog"};
        //}
        //
        //private string firstWriteTLog { get => $"{typeof(ZigCompile).FullName}.write.1.tlog"; }
        //protected override string[] WriteTLogNames
        //{
        //    get => new string[] {
        //            firstWriteTLog,
        //            $"{SubTool}.write.*.tlog",
        //            $"{SubTool}.*.write.*.tlog",
        //            $"{SubTool}-*.write.*.tlog" };
        //}
        //
        //protected override string CommandTLogName
        //{
        //    get => $"{SubTool}.command.1.tlog";
        //}

        protected override bool TrackReplaceFile { get => true; }

        protected override void RemoveTaskSpecificInputs(CanonicalTrackedInputFiles compactInputs)
        {
            if (base.IsPropertySet("PrecompiledHeader") && this.PrecompiledHeader != "Create")
            {
                return;
            }
            if (base.IsPropertySet("ObjectFileName"))
            {
                string objectFileName = this.ObjectFileName;
                TaskItem taskItem = new TaskItem(objectFileName);
                compactInputs.RemoveDependencyFromEntry(this.Sources, taskItem);
                return;
            }
        }

        protected override int ExecuteTool(string pathToTool, string responseFileCommands, string commandLineCommands)
        {
            foreach (ITaskItem taskItem in base.SourcesCompiled)
            {
                Log.LogMessage(MessageImportance.High, Path.GetFileName(taskItem.ItemSpec), Array.Empty<object>());
            }

            //var firstReadTlogPath = Path.Combine(TrackerIntermediateDirectory, firstReadTLog);
            //var firstWriteTlogPath = Path.Combine(TrackerIntermediateDirectory, firstWriteTLog);
            //
            //for (int attempt = 0; attempt < 30; attempt++)
            //{
            //    if (!File.Exists(firstReadTlogPath))
            //    {
            //        try 
            //        {
            //            using (File.Create(firstReadTlogPath))
            //            {
            //            }
            //        }
            //        catch (IOException)
            //        {
            //            Thread.Sleep(50);
            //            continue;
            //        }
            //    }
            //
            //    if (!File.Exists(firstWriteTlogPath))
            //    {
            //        try
            //        {
            //            using (File.Create(firstWriteTlogPath))
            //            {
            //            }
            //        }
            //        catch (IOException)
            //        {
            //            Thread.Sleep(50);
            //            continue;
            //        }
            //    }
            //
            //    break;
            //}

            errorListRegexList.Add(clangMessageRegex);
            return base.ExecuteTool(pathToTool, responseFileCommands, commandLineCommands);
        }

        protected override string GenerateResponseFileCommandsExceptSwitches(string[] switchesToRemove,
                                                                             CommandLineFormat format = CommandLineFormat.ForBuildLog,
                                                                             EscapeFormat escapeFormat = EscapeFormat.EscapeTrailingSlash)
        {
            var text = base.GenerateResponseFileCommandsExceptSwitches(switchesToRemove, format, EscapeFormat.EscapeTrailingSlash);
            text = FindBackSlashInPath.Replace(text, "\\\\");
            return text;
        }

        protected static Regex clangMessageRegex = new Regex(
            "^\\s*(?<FILENAME>[^:]*):(?<LINE>\\d*):(?<COLUMN>\\d*)\\s*:\\s*(?<CATEGORY>fatal error|error|warning|note):(?<TEXT>.*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
    }
}
