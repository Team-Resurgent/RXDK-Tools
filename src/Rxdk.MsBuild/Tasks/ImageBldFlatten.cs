using Microsoft.Build.Framework;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// imagebld's "/DXT &lt;input&gt; &lt;output&gt;" mode: a totally separate command-line shape
    /// from the normal "/out: /in: /stack: ..." XBE-wrapping invocation (see
    /// Rxdk.XbeImage.ImageBldOptionsParser.ParseDxtArguments -- /DXT must be the very first
    /// argument, and only /IN:, /OUT:, or two positional paths are accepted after it). A DXT
    /// (debug-monitor extension) skips the XBE-wrapping step entirely: it flattens the linked .exe
    /// directly -- every section laid down at its RVA (file offset == RVA), Xbox subsystem set --
    /// so xbdm's raw-file loader can jump straight to DxtEntry.
    /// </summary>
    public class ImageBldFlatten : RxdkToolTask
    {
        protected override string ToolName => "imagebld.exe";

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        [Required]
        public virtual string OutputFile { get; set; }

        protected override string GenerateCommandLineCommands() =>
            $"/DXT {InputFile.ItemSpec} {OutputFile}";

        protected override string GenerateResponseFileCommands() => string.Empty;

        protected override ITaskItem[] TrackedInputFiles => new ITaskItem[] { InputFile };
    }
}
