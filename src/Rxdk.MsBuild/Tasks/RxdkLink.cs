using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Links objects into an Xbox executable with clang (driving ld.lld). Builds the
    /// command line explicitly, preserving the exact switch set and ordering the previous
    /// TrackedVCToolTask-based task emitted -- in particular the whole-archive begin/end pair
    /// wraps AdditionalOptions + Sources + AdditionalDependencies, with LibraryDependencies
    /// (the -lNAME list) after the wrap.
    /// </summary>
    public class RxdkLink : RxdkCompilerTask
    {
        public virtual string OutputFile { get; set; }
        public virtual bool ShowProgress { get; set; }
        public virtual bool Version { get; set; }
        public virtual bool VerboseOutput { get; set; }
        public virtual bool Trace { get; set; }
        public virtual string[] TraceSymbols { get; set; }
        public virtual string EntryPointSymbol { get; set; }
        public virtual bool PrintMap { get; set; }
        public virtual string LinkerScript { get; set; }
        public virtual bool UnresolvedSymbolReferences { get; set; }
        public virtual string[] LibraryPath { get; set; }
        public virtual bool OptimizeforMemory { get; set; }
        public virtual string[] AdditionalLibraryDirectories { get; set; } // accepted, not emitted (matches previous task)
        public virtual string[] IgnoreSpecificDefaultLibraries { get; set; }
        public virtual bool ForceUndefineSymbolReferences { get; set; }
        public virtual string DebuggerSymbolInformation { get; set; }
        public virtual string GenerateMapFile { get; set; }

        private bool _reloc, _relocSet;
        public virtual bool Relocation { get => _reloc; set { _reloc = value; _relocSet = true; } }

        public virtual bool FunctionBinding { get; set; }
        public virtual bool NoExecStackRequired { get; set; }
        public virtual bool WholeArchiveBegin { get; set; }
        public virtual string[] AdditionalDependencies { get; set; }
        public virtual bool WholeArchiveEnd { get; set; }
        public virtual string[] LibraryDependencies { get; set; }
        public virtual string AdditionalOptions { get; set; }

        private static readonly string[] AlwaysAppendList =
        {
            "-nostdlib", "-nostartfiles",
            "-Wl,--image-base=0x10000",
            // Pin lld (the fork clang links -nostdlib and takes no system linker); the compiler-rt
            // builtins the SDK libs need are appended explicitly at the end of the link (below).
            "-fuse-ld=lld",
        };

        private static readonly Dictionary<string, string> DebuggerSymbolInformationMap = new Dictionary<string, string>
        {
            {"true",""},{"false",""},{"IncludeAll",""},
            {"OmitDebuggerSymbolInformation","-Wl,--strip-debug"},
            {"OmitAllSymbolInformation","-Wl,--strip-all"},
        };

        protected static readonly Regex ldMessageRegex = new Regex(
            "^\\s*(?<FILENAME>[^:]*):(((?<LINE>\\d*):)?)(\\s*(?<CATEGORY>(fatal error|error|warning|note)):)?\\s*(?<TEXT>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100.0));

        public override bool Execute()
        {
            if (Sources == null || Sources.Length == 0)
                return true;

            var root = ResolveLlvmRoot();
            if (root == null)
                return false;

            var a = new List<string>();

            Opt(a, "-target ", Target);
            a.Add(Machine);
            Opt(a, "-o ", OutputFile);
            a.AddRange(AlwaysAppendList);
            Flag(a, ShowProgress, "-Wl,--stats");
            Flag(a, Version, "-Wl,--version");
            Flag(a, VerboseOutput, "--verbose");
            Flag(a, Trace, "-Wl,--trace");
            OptList(a, "-Wl,--trace-symbol=", TraceSymbols);
            Opt(a, "-Wl,--entry=", EntryPointSymbol);
            Flag(a, PrintMap, "-Wl,--print-map");
            Opt(a, "-T ", LinkerScript);
            Flag(a, UnresolvedSymbolReferences, "-Wl,--no-undefined");
            Flag(a, OptimizeforMemory, "-Wl,--no-keep-memory");
            OptList(a, "-L ", LibraryPath);
            OptList(a, "-Wl,--exclude-libs=", IgnoreSpecificDefaultLibraries);
            Flag(a, ForceUndefineSymbolReferences, "-Wl,-u--undefined=");
            Mapped(a, DebuggerSymbolInformationMap, DebuggerSymbolInformation);
            Opt(a, "-Wl,-Map=", GenerateMapFile);
            FlagRev(a, _relocSet, _reloc, "-Wl,-z,relro", "-Wl,-z,norelro");
            Flag(a, FunctionBinding, "-Wl,-z,now");
            Flag(a, NoExecStackRequired, "-Wl,-z,noexecstack");
            Flag(a, WholeArchiveBegin, "-Wl,--whole-archive");
            Raw(a, AdditionalOptions);
            foreach (ITaskItem src in Sources)
                a.Add(Quote(src.GetMetadata("FullPath")));
            // Additional Dependencies were emitted UNQUOTED with an empty switch by the previous
            // task (AppendSwitchUnquotedIfNotNull("", value)); reproduce that verbatim.
            if (AdditionalDependencies != null)
                foreach (var dep in AdditionalDependencies)
                    if (!string.IsNullOrWhiteSpace(dep))
                        a.Add(dep.Trim());
            Flag(a, WholeArchiveEnd, "-Wl,--no-whole-archive");
            OptList(a, "-l", LibraryDependencies);
            // compiler-rt builtins: satisfies the SDK libs' 64-bit integer + stack-probe builtins
            // (__divdi3/__alloca/…). Placed AFTER the libs so lld resolves their undefined refs; a
            // plain archive, pulled on demand (libcompat's whole-archive overrides still win). zig
            // auto-linked its bundled compiler-rt here; a bare clang link must add it explicitly.
            var builtins = BuiltinsArchive(root);
            if (builtins != null)
                a.Add(Quote(builtins));

            var r = Run(ClangExe(root), a, workingDir: null, useResponseFile: true);
            LogDiagnostics(r.Combined, new[] { ldMessageRegex });
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("clang (link) failed with exit code {0}", r.ExitCode);
            return !Log.HasLoggedErrors && r.ExitCode == 0;
        }
    }
}
