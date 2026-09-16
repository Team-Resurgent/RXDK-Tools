using System.Diagnostics;
using System.IO;
using Microsoft.Build.Framework;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Compiles one XACT project (.xap) into the XactSounds.h + .xwb/.xsb it names internally,
    /// matching XboxBuild.cs's CompileXactProjectsAsync. Unlike RxdkBundler, xactbld takes the
    /// .xap's absolute path rather than a bare filename, but still runs with the .xap's own
    /// directory as the working directory (its outputs are relative to it).
    /// </summary>
    public class RxdkXactBld : RxdkToolTask
    {
        protected override string ToolName => "xactbld.exe";

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        protected override string GenerateCommandLineCommands() =>
            $"\"{InputFile.GetMetadata("FullPath")}\" -q";

        protected override string GenerateResponseFileCommands() => string.Empty;

        protected override ITaskItem[] TrackedInputFiles => new ITaskItem[] { InputFile };

        protected override ProcessStartInfo GetProcessStartInfo(string pathToTool, string commandLineCommands, string responseFileSwitch)
        {
            var psi = base.GetProcessStartInfo(pathToTool, commandLineCommands, responseFileSwitch);
            psi.WorkingDirectory = Path.GetDirectoryName(InputFile.GetMetadata("FullPath"));
            return psi;
        }
    }
}
