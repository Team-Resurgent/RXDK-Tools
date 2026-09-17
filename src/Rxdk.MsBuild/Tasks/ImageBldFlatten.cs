using Microsoft.Build.Framework;
using System.Collections.Generic;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// imagebld's "/DXT &lt;input&gt; &lt;output&gt;" mode: a totally separate command-line shape
    /// from the normal "/out: /in: /stack: ..." XBE-wrapping invocation. /DXT must be the very
    /// first argument. A DXT (debug-monitor extension) skips the XBE-wrapping step entirely: it
    /// flattens the linked .exe directly so xbdm's raw-file loader can jump straight to DxtEntry.
    /// </summary>
    public class ImageBldFlatten : RxdkToolTask
    {
        protected string ToolName => "imagebld.exe";

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        [Required]
        public virtual string OutputFile { get; set; }

        public override bool Execute()
        {
            var exe = GetToolExe(ToolName);
            if (exe == null)
                return false;

            var a = new List<string> { "/DXT", Quote(InputFile.ItemSpec), Quote(OutputFile) };
            var r = Run(exe, a);
            LogDiagnostics(r.Combined, new System.Text.RegularExpressions.Regex[0]);
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("imagebld /DXT failed with exit code {0}", r.ExitCode);
            return !Log.HasLoggedErrors && r.ExitCode == 0;
        }
    }
}
