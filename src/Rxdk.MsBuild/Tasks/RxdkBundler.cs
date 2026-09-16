using System.Diagnostics;
using System.IO;
using Microsoft.Build.Framework;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Compiles one .rdf resource-description file into the Resource.h + .xpr pair its own
    /// out_header/out_packedresource directives name (or bundler's own name-derived defaults when
    /// a directive is absent), matching XboxBuild.cs's CompileResourcesAsync: bundler resolves
    /// those output paths relative to the .rdf itself, so it must run with the .rdf's own
    /// directory as the working directory, given only the bare on-disk filename -- not a full or
    /// project-relative path.
    /// </summary>
    public class RxdkBundler : RxdkToolTask
    {
        protected override string ToolName => "bundler.exe";

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        protected override string GenerateCommandLineCommands() =>
            $"{InputFile.GetMetadata("Filename")}{InputFile.GetMetadata("Extension")} -q";

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
