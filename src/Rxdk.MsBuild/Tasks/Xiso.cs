using Microsoft.Build.Framework;
using System.Collections.Generic;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Packs a staged directory into an Xbox ISO with xdvdfs ("pack &lt;dir&gt; &lt;out.iso&gt;").
    /// </summary>
    public class Xiso : RxdkToolTask
    {
        protected string ToolName => "xdvdfs.exe";

        public virtual string OutputFile { get; set; }

        [Required]
        public virtual ITaskItem InputDirectory { get; set; }

        public override bool Execute()
        {
            var exe = GetToolExe(ToolName);
            if (exe == null)
                return false;

            var a = new List<string> { "pack", Quote(InputDirectory.ItemSpec), Quote(OutputFile) };
            var r = Run(exe, a);
            LogDiagnostics(r.Combined, new System.Text.RegularExpressions.Regex[0]);
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("xdvdfs pack failed with exit code {0}", r.ExitCode);
            return !Log.HasLoggedErrors && r.ExitCode == 0;
        }
    }
}
