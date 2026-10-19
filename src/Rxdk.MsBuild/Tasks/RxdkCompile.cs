using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Rxdk.Engine.Build;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Compiles C/C++/asm sources with clang. Builds the clang command line explicitly
    /// (preserving the exact flags/order the previous TrackedVCToolTask-based task emitted),
    /// runs it through a response file (backslashes doubled), and does its own header-aware
    /// incremental up-to-date check from the generated depfile.
    /// </summary>
    public class RxdkCompile : RxdkCompilerTask
    {
        // ---- properties (names/types identical to the previous task, so the .targets and
        //      property-page .xml that pass them by name need no change) --------------------

        public virtual string Optimization { get; set; }
        public virtual string DebugInfo { get; set; }
        public virtual string[] AdditionalIncludeDirectories { get; set; }
        public virtual string ObjectFileName { get; set; }
        public virtual string WarningLevel { get; set; }
        public virtual bool TreatWarningAsError { get; set; }
        public virtual string[] DisableSpecificWarnings { get; set; }      // accepted, not on the old command line
        public virtual bool Verbose { get; set; }
        public virtual string TrackerLogDirectory { get; set; }            // accepted no-op (no Tracker.exe)

        private bool _omitFP, _omitFPSet;
        public virtual bool OmitFramePointers { get => _omitFP; set { _omitFP = value; _omitFPSet = true; } }

        public virtual bool FunctionLevelLinking { get; set; }
        public virtual bool DataLevelLinking { get; set; }
        public virtual bool BufferSecurityCheck { get; set; }              // accepted, not on the old command line

        private bool _rtti, _rttiSet;
        public virtual bool RuntimeTypeInfo { get => _rtti; set { _rtti = value; _rttiSet = true; } }

        public virtual string CLanguageStandard { get; set; }
        public virtual string CppLanguageStandard { get; set; }
        public virtual string[] PreprocessorDefinitions { get; set; }
        public virtual string[] UndefinePreprocessorDefinitions { get; set; }
        public virtual bool UndefineAllPreprocessorDefinitions { get; set; }
        public virtual string PrecompiledHeader { get; set; }              // accepted no-op (PCH unused)
        public virtual string PrecompiledHeaderFile { get; set; }          // accepted no-op
        public virtual string PrecompiledHeaderOutputFileDirectory { get; set; } // accepted no-op
        public virtual string PrecompiledHeaderCompileAs { get; set; }
        public virtual string CompileAs { get; set; }
        public virtual string[] ForcedIncludeFiles { get; set; }
        public virtual string AdditionalOptions { get; set; }

        // Compiled verbatim: always-appended flags (was AlwaysAppend). Every entry preserved.
        private static readonly string[] AlwaysAppendList =
        {
            // want to just compile files right now
            "-c",

            "-ffreestanding", "-fno-stack-protector", "-fms-extensions", "-fms-compatibility",
            "-fms-compatibility-version=19.44",
            "-nostdinc", "-march=pentium3",
            // long double is the 64-bit double type, as with the XDK compiler; must match
            // Rxdk.Engine.Build.XboxTarget.CodegenFlags, which builds the SDK libraries.
            "-mlong-double-64",
            // Every Xbox title is built with _XBOX/XBOX defined (the XDK did this); a lot of
            // Xbox headers/code select their platform path on it.
            "-D_XBOX", "-DXBOX",
            // Keep Clang from inline-expanding memmove/memcpy-shaped calls past picolibc's
            // -fno-builtin implementations, and pin the retail (_DEBUG-off) SDK link path.
            "-fno-builtin",
            // picolibc's default assert() calls __assert_no_args(), which prints a bare
            // "assertion failed" -- useless for locating a fault in a title. Ask for the
            // variant that reports the expression, file and line.
            "-D__ASSERT_VERBOSE",
            // Thread-local storage: emulated TLS (a per-thread table reached via
            // __emutls_get_address, backed by libc tss/emutls.c) instead of the native
            // Windows __tls_index/TEB %fs model, which the RXDK runtime never sets up.
            "-femulated-tls",

            "-fno-sanitize=undefined",

            // -I (not -isystem) everywhere: the SDK's clean-room windef.h/etc. must win over the toolchain's
            // bundled MinGW headers, which -isystem would let shadow them.
            "-Wno-c++11-narrowing",
            "-Wno-address-of-temporary",
            "-Wno-ignored-pragma-intrinsic",
            "-Wno-multichar",
            "-Wno-unused-command-line-argument",
            "-Wno-deprecated-enum-enum-conversion",
        };

        private static readonly Dictionary<string, string> OptimizationMap = new Dictionary<string, string>
        {
            {"None", "-O0"}, {"FavorSpeed", "-O2"}, {"FavorSize", "-Os"}, {"Full", "-O3"},
        };

        private static readonly Dictionary<string, string> DebugInfoMap = new Dictionary<string, string>
        {
            {"None", ""}, {"LineTables", "-gline-tables-only"}, {"Full", "-g"},
        };

        private static readonly Dictionary<string, string> WarningLevelMap = new Dictionary<string, string>
        {
            {"TurnOffAllWarnings", "-w"}, {"EnableDefaultWarnings", ""},
            {"EnableAllWarnings", "-Wall"}, {"EnableExtraWarnings", "-Wall -Wextra"},
        };

        private static readonly Dictionary<string, string> CLanguageStandardMap = new Dictionary<string, string>
        {
            {"Default",""},{"c89","-std=c89"},{"iso9899:199409","-std=iso9899:199409"},{"gnu89","-std=gnu89"},
            {"c99","-std=c99"},{"gnu99","-std=gnu99"},{"c11","-std=c11"},{"gnu11","-std=gnu11"},
            {"c17","-std=c17"},{"gnu17","-std=gnu17"},{"c23","-std=c23"},{"gnu23","-std=gnu23"},
            {"c2y","-std=c2y"},{"gnu2y","-std=gnu2y"},
        };

        private static readonly Dictionary<string, string> CppLanguageStandardMap = new Dictionary<string, string>
        {
            {"Default",""},{"c++98","-std=c++98"},{"gnu++98","-std=gnu++98"},{"c++11","-std=c++11"},{"gnu++11","-std=gnu++11"},
            {"c++14","-std=c++14"},{"gnu++14","-std=gnu++14"},{"c++17","-std=c++17"},{"gnu++17","-std=gnu++17"},
            {"c++20","-std=c++20"},{"gnu++20","-std=gnu++20"},{"c++23","-std=c++23"},{"gnu++23","-std=gnu++23"},
            {"c++2c","-std=c++2c"},{"gnu++2c","-std=gnu++2c"},{"c++2d","-std=c++2d"},{"gnu++2d","-std=gnu++2d"},
        };

        private static readonly Dictionary<string, string> PrecompiledHeaderCompileAsMap = new Dictionary<string, string>
        {
            {"CompileAsC", "-x c-header"}, {"CompileAsCpp", "-x c++-header"},
        };

        private static readonly Dictionary<string, string> CompileAsMap = new Dictionary<string, string>
        {
            {"Default",""},{"CompileAsC","-x c"},{"CompileAsCpp","-x c++"},{"CompileAsAsm","-x assembler-with-cpp"},
        };

        protected static readonly Regex clangMessageRegex = new Regex(
            "^\\s*(?<FILENAME>[^:]*):(?<LINE>\\d*):(?<COLUMN>\\d*)\\s*:\\s*(?<CATEGORY>fatal error|error|warning|note):(?<TEXT>.*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override bool Execute()
        {
            if (Sources == null || Sources.Length == 0)
                return true;

            var root = ResolveLlvmRoot();
            if (root == null)
                return false;

            foreach (ITaskItem src in Sources)
                Log.LogMessage(MessageImportance.High, Path.GetFileName(src.ItemSpec));

            var a = BuildArgs(root);
            var clang = ClangExe(root);

            // Incremental skip (shared with the engine build, see ObjFreshness): the object is
            // current only when it is newer than every source and every header its depfile lists,
            // and was built by this exact command line -- a changed define, include directory,
            // option or configuration rebuilds it. Missing depfile or command record => rebuild.
            var obj = SingleObjectFile();
            var signature = ObjFreshness.Signature(clang, a);
            if (obj != null)
            {
                var sources = new List<string>();
                foreach (ITaskItem src in Sources)
                    sources.Add(src.GetMetadata("FullPath"));
                if (ObjFreshness.IsUpToDate(sources, obj, signature))
                {
                    Log.LogMessage(MessageImportance.Low, "  up to date");
                    return true;
                }
                // Drop the old record first: if this compile fails, the stale object must not
                // look current to the next build.
                File.Delete(ObjFreshness.CommandFile(obj));
            }

            var r = Run(clang, a, workingDir: null, useResponseFile: true);
            LogDiagnostics(r.Combined, new[] { clangMessageRegex });
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("clang (compile) failed with exit code {0}", r.ExitCode);
            var ok = !Log.HasLoggedErrors && r.ExitCode == 0;
            if (ok && obj != null)
                File.WriteAllText(ObjFreshness.CommandFile(obj), signature);
            return ok;
        }

        /// <summary>The object this invocation writes, or null when ObjectFileName names a directory.</summary>
        private string SingleObjectFile()
        {
            if (string.IsNullOrWhiteSpace(ObjectFileName)) return null;
            var obj = ObjectFileName;
            return obj.EndsWith("\\") || obj.EndsWith("/") ? null : obj;
        }

        private List<string> BuildArgs(string root)
        {
            var a = new List<string>();

            Opt(a, "-target ", Target);
            a.Add(Machine);
            Mapped(a, OptimizationMap, Optimization);
            a.AddRange(AlwaysAppendList);
            // -nostdinc (above) also drops clang's own resource dir, so re-add it (stddef.h/…).
            var res = ResourceInclude(root);
            if (res != null) { a.Add("-isystem"); a.Add(res); }
            Mapped(a, DebugInfoMap, DebugInfo);
            OptList(a, "-I ", AdditionalIncludeDirectories);
            if (!string.IsNullOrWhiteSpace(ObjectFileName))
            {
                Opt(a, "-o ", ObjectFileName);
                // Header-dependency depfile for the incremental check above.
                a.Add("-MD");
                Opt(a, "-MF ", ObjectFileName + ".d");
            }
            Mapped(a, WarningLevelMap, WarningLevel);
            Flag(a, TreatWarningAsError, "-Werror");
            Flag(a, Verbose, "-v");
            FlagRev(a, _omitFPSet, _omitFP, "-fomit-frame-pointer", "-fno-omit-frame-pointer");
            Flag(a, FunctionLevelLinking, "-ffunction-sections");
            Flag(a, DataLevelLinking, "-fdata-sections");
            FlagRev(a, _rttiSet, _rtti, "-frtti", "-fno-rtti");
            Mapped(a, CLanguageStandardMap, CLanguageStandard);
            Mapped(a, CppLanguageStandardMap, CppLanguageStandard);
            OptList(a, "-D ", PreprocessorDefinitions);
            OptList(a, "-U ", UndefinePreprocessorDefinitions);
            Flag(a, UndefineAllPreprocessorDefinitions, "-undef");
            Mapped(a, PrecompiledHeaderCompileAsMap, PrecompiledHeaderCompileAs);
            Mapped(a, CompileAsMap, CompileAs);
            OptList(a, "-include ", ForcedIncludeFiles);
            Raw(a, AdditionalOptions);
            foreach (ITaskItem src in Sources)
                a.Add(Quote(src.GetMetadata("FullPath")));

            return a;
        }
    }
}
