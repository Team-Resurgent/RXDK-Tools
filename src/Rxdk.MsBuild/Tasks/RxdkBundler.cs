using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Compiles one .rdf resource-description file into the Resource.h + .xpr pair it names.
    /// bundler resolves those output paths relative to the .rdf itself, so it must run with the
    /// .rdf's own directory as the working directory, given only the bare on-disk filename.
    /// </summary>
    public class RxdkBundler : RxdkToolTask
    {
        protected string ToolName => "bundler.exe";

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        public override bool Execute()
        {
            var exe = GetToolExe(ToolName);
            if (exe == null)
                return false;

            var name = InputFile.GetMetadata("Filename") + InputFile.GetMetadata("Extension");
            var workDir = Path.GetDirectoryName(InputFile.GetMetadata("FullPath"));
            var a = new List<string> { Quote(name), "-q" };

            var r = Run(exe, a, workingDir: workDir);
            LogDiagnostics(r.Combined, new System.Text.RegularExpressions.Regex[0]);
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("bundler failed with exit code {0}", r.ExitCode);
            return !Log.HasLoggedErrors && r.ExitCode == 0;
        }
    }
}
