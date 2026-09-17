using Microsoft.Build.Framework;
using System.Collections.Generic;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Wraps a linked .exe into an .xbe with imagebld. Builds the "/out: /in: /stack: ..."
    /// command line explicitly, preserving the exact switches and ordering the previous task used.
    /// </summary>
    public class ImageBld : RxdkToolTask
    {
        protected string ToolName => "imagebld.exe";

        public virtual string OutputFile { get; set; }

        [Required]
        public virtual ITaskItem InputFile { get; set; }

        private int _stackSize; private bool _stackSizeSet;
        public int StackSize { get => _stackSize; set { _stackSize = value; _stackSizeSet = true; } }

        public bool Debug { get; set; }
        public bool NoLogo { get; set; }
        public bool NoLibWarn { get; set; }
        public bool LimitMemory { get; set; }
        public bool DontModifyHardDisk { get; set; }
        public bool DontMountUtilityDrive { get; set; }
        public bool FormatUtilityDrive { get; set; }
        public string UtilityDriveClusterSize { get; set; }
        public string[] NoPreload { get; set; }
        public string TestId { get; set; }
        public string TestAltId { get; set; }
        public string TestRegion { get; set; }
        public string TestRatings { get; set; }
        public string TestMediaTypes { get; set; }
        public string TestLanKey { get; set; }
        public string TestSignKey { get; set; }
        public string TestName { get; set; }
        public string TestVersion { get; set; }
        public string TitleInfo { get; set; }
        public string TitleImage { get; set; }
        public string DefaultSaveImage { get; set; }

        /// <summary>Pre-built "path,name,R"-style imagebld /INSERTFILE arguments, passed through opaquely.</summary>
        public string[] InsertFiles { get; set; }

        private static readonly Dictionary<string, string> UtilityDriveClusterSizeMap = new Dictionary<string, string>
        {
            {"Default",""},{"16KB","16384"},{"32KB","32768"},{"64KB","65536"},{"128KB","131072"},{"256KB","262144"},
        };

        public override bool Execute()
        {
            var exe = GetToolExe(ToolName);
            if (exe == null)
                return false;

            var a = new List<string>();
            Opt(a, "/out:", OutputFile);
            if (InputFile != null)
                a.Add("/in:" + Quote(InputFile.ItemSpec));
            if (_stackSizeSet && _stackSize > 0)
                a.Add("/stack:" + _stackSize);
            Flag(a, Debug, "/debug");
            Flag(a, NoLogo, "/nologo");
            Flag(a, NoLibWarn, "/nolibwarn");
            Flag(a, LimitMemory, "/limitmem");
            Flag(a, DontModifyHardDisk, "/dontmodifyhd");
            Flag(a, DontMountUtilityDrive, "/dontmountud");
            Flag(a, FormatUtilityDrive, "/formatud");
            AddClusterSize(a);
            OptList(a, "/nopreload:", NoPreload);
            Opt(a, "/testid:", TestId);
            Opt(a, "/testaltid:", TestAltId);
            Opt(a, "/testregion:", TestRegion);
            Opt(a, "/testratings:", TestRatings);
            Opt(a, "/testmediatypes:", TestMediaTypes);
            Opt(a, "/testlankey:", TestLanKey);
            Opt(a, "/testsignkey:", TestSignKey);
            Opt(a, "/testname:", TestName);
            Opt(a, "/testversion:", TestVersion);
            Opt(a, "/titleinfo:", TitleInfo);
            Opt(a, "/titleimage:", TitleImage);
            Opt(a, "/defaultsaveimage:", DefaultSaveImage);
            OptList(a, "/insertfile:", InsertFiles);

            var r = Run(exe, a);
            LogDiagnostics(r.Combined, new System.Text.RegularExpressions.Regex[0]);
            if (r.ExitCode != 0 && !Log.HasLoggedErrors)
                Log.LogError("imagebld failed with exit code {0}", r.ExitCode);
            return !Log.HasLoggedErrors && r.ExitCode == 0;
        }

        private void AddClusterSize(List<string> a)
        {
            if (string.IsNullOrWhiteSpace(UtilityDriveClusterSize)) return;
            string v = UtilityDriveClusterSize.Trim();
            if (UtilityDriveClusterSizeMap.TryGetValue(v, out var mapped))
            {
                if (!string.IsNullOrEmpty(mapped)) a.Add("/udcluster:" + mapped);
            }
            else if (int.TryParse(v, out _))
            {
                a.Add("/udcluster:" + v);
            }
        }
    }
}
