using Microsoft.Build.Framework;
using System.Collections.Generic;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Archives objects into a static library with "zig ar". Builds the command line explicitly,
    /// preserving the previous task's switch set and ordering (no Target/Machine for the archiver).
    /// </summary>
    public class ZigAr : ZigToolTask
    {
        public override string SubTool => "ar";

        public virtual string Command { get; set; }
        public virtual bool CreateIndex { get; set; }
        public virtual bool CreateThinArchive { get; set; }
        public virtual bool NoWarnOnCreate { get; set; }
        public virtual bool TruncateTimestamp { get; set; }

        private bool _suppress, _suppressSet;
        public virtual bool SuppressStartupBanner { get => _suppress; set { _suppress = value; _suppressSet = true; } }

        public virtual bool Verbose { get; set; }
        public virtual string AdditionalOptions { get; set; }
        public virtual string OutputFile { get; set; }

        private static readonly Dictionary<string, string> CommandMap = new Dictionary<string, string>
        {
            {"Delete","-d"},{"Move","-m"},{"Print","-p"},{"Quick","-q"},
            {"Replacement","-r"},{"Table","-t"},{"Extract","-x"},
        };

        public override bool Execute()
        {
            if (Sources == null || Sources.Length == 0)
                return true;

            var zig = ResolveZig();
            if (zig == null)
                return false;

            var a = new List<string>();

            Mapped(a, CommandMap, Command);
            a.Add("-r"); // AlwaysAppend
            Flag(a, CreateIndex, "-s");
            Flag(a, CreateThinArchive, "-T");
            Flag(a, NoWarnOnCreate, "-c");
            Flag(a, TruncateTimestamp, "-D");
            // SuppressStartupBanner is a reverse-only switch: it emits -V (show version) only when
            // explicitly set to false, and nothing when true or unset -- reproduce verbatim.
            FlagRev(a, _suppressSet, _suppress, "", "-V");
            Flag(a, Verbose, "-v");
            Raw(a, AdditionalOptions);
            Opt(a, "", OutputFile);
            foreach (ITaskItem src in Sources)
                a.Add(Quote(src.GetMetadata("FullPath")));

            var r = Run(zig, a, workingDir: null, useResponseFile: true, leadingArgs: new[] { SubTool },
                        doubleBackslashes: false);
            LogDiagnostics(r.Combined, new System.Text.RegularExpressions.Regex[0]);
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("zig ar failed with exit code {0}", r.ExitCode);
            return !Log.HasLoggedErrors && r.ExitCode == 0;
        }
    }
}
