using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Compiles one XACT project (.xap) into the XactSounds.h + .xwb/.xsb it names internally.
    /// Unlike RxdkBundler, xactbld takes the .xap's absolute path rather than a bare filename,
    /// but still runs with the .xap's own directory as the working directory.
    /// </summary>
    public class RxdkXactBld : RxdkToolTask
    {
        protected string ToolName => "xactbld.exe";

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        public override bool Execute()
        {
            var exe = GetToolExe(ToolName);
            if (exe == null)
                return false;

            var full = InputFile.GetMetadata("FullPath");
            var workDir = Path.GetDirectoryName(full);
            var a = new List<string> { Quote(full), "-q" };

            var r = Run(exe, a, workingDir: workDir);
            LogDiagnostics(r.Combined, new System.Text.RegularExpressions.Regex[0]);
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("xactbld failed with exit code {0}", r.ExitCode);
            return !Log.HasLoggedErrors && r.ExitCode == 0;
        }
    }
}
