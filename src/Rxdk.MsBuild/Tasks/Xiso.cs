using Microsoft.Build.CPPTasks;
using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxdk.MsBuild.Tasks
{
    public class Xiso : RxdkToolTask
    {
        protected override string ToolName => "xdvdfs.exe";

        public virtual string OutputFile
        {
            get => PropertyOrNull<string>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.File),
                    value
                );
            }
        }
        
        [Required]
        public virtual ITaskItem InputDirectory
        {
            get => PropertyOrNull<ITaskItem>();
            set
            {
                UpdateSwitch(
                    new ToolSwitch(ToolSwitchType.ITaskItem),
                    value
                );
            }
        }

        protected override string GenerateCommandLineCommands()
        {
            return $"pack {InputDirectory} {OutputFile}";
        }

        protected override string GenerateResponseFileCommands()
        {
            return string.Empty;
        }

        protected override ITaskItem[] TrackedInputFiles => new ITaskItem[] { InputDirectory };
    }
}
