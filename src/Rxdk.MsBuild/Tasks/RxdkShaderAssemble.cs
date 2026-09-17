using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Assembles one Xbox vertex/pixel shader source file (.vsh/.psh) into its binary form
    /// (.xvu/.xpu) with xsasm. -I points xsasm at the source's own directory so sibling #include
    /// fragments resolve; the source dir is also the working directory.
    /// </summary>
    public class RxdkShaderAssemble : RxdkToolTask
    {
        protected string ToolName => "xsasm.exe";

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        [Required]
        public virtual string OutputFile { get; set; }

        public override bool Execute()
        {
            var exe = GetToolExe(ToolName);
            if (exe == null)
                return false;

            var name = InputFile.GetMetadata("Filename") + InputFile.GetMetadata("Extension");
            var inputDir = Path.GetDirectoryName(InputFile.GetMetadata("FullPath"));
            var a = new List<string> { Quote(name), "-o", Quote(OutputFile), "-I", Quote(inputDir) };

            var r = Run(exe, a, workingDir: inputDir);
            LogDiagnostics(r.Combined, new System.Text.RegularExpressions.Regex[0]);
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("xsasm failed with exit code {0}", r.ExitCode);
            return !Log.HasLoggedErrors && r.ExitCode == 0;
        }
    }
}
