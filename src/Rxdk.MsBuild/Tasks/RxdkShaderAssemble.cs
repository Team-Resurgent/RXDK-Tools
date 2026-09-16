using System.Diagnostics;
using System.IO;
using Microsoft.Build.Framework;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Assembles one Xbox vertex/pixel shader source file (.vsh/.psh) into its binary form
    /// (.xvu/.xpu), matching XboxBuild.cs's CompileShadersAsync. -I points xsasm at the source's
    /// own directory so sibling #include fragments resolve, same as the old engine.
    /// </summary>
    public class RxdkShaderAssemble : RxdkToolTask
    {
        protected override string ToolName => "xsasm.exe";

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        [Required]
        public virtual string OutputFile { get; set; }

        private string InputDir => Path.GetDirectoryName(InputFile.GetMetadata("FullPath"));

        protected override string GenerateCommandLineCommands() =>
            $"{InputFile.GetMetadata("Filename")}{InputFile.GetMetadata("Extension")} -o \"{OutputFile}\" -I \"{InputDir}\"";

        protected override string GenerateResponseFileCommands() => string.Empty;

        protected override ITaskItem[] TrackedInputFiles => new ITaskItem[] { InputFile };

        protected override ProcessStartInfo GetProcessStartInfo(string pathToTool, string commandLineCommands, string responseFileSwitch)
        {
            var psi = base.GetProcessStartInfo(pathToTool, commandLineCommands, responseFileSwitch);
            psi.WorkingDirectory = InputDir;
            return psi;
        }
    }
}
