using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Resolves the XDK "Additional Files" deploy list into flat files to stage into the ISO,
    /// matching the engine's PackXiso.ResolveDeployPaths so VS Code and VS20XX agree.
    ///
    /// Each input item follows the form <c>[Destination=]Source</c>:
    ///   - <c>Source</c> alone lands under its own cleaned relative name in the image;
    ///   - <c>Destination=Source</c> places <c>Source</c> (a host path) at <c>Destination</c> (an
    ///     image-relative path) instead -- for a file that is the full target path incl. filename,
    ///     for a directory the target directory the tree lands under.
    /// The split is on the FIRST '=', unambiguous because a Windows source path never contains '='.
    ///
    /// Each output item's ItemSpec is the host source file; its IsoDest metadata is the full
    /// image-relative destination (so the staging Copy can place/rename it precisely). Directories
    /// are walked at execution time (after RxdkBuildResources), so generated media is picked up.
    /// </summary>
    public class ResolveDeployFiles : Task
    {
        [Required] public string ProjectDir { get; set; }

        public ITaskItem[] DeployPaths { get; set; }

        [Output] public ITaskItem[] DeployFiles { get; set; }

        public override bool Execute()
        {
            var outList = new List<ITaskItem>();
            foreach (var item in DeployPaths ?? new ITaskItem[0])
            {
                var raw = item?.ItemSpec;
                if (string.IsNullOrWhiteSpace(raw)) continue;

                // Split the optional "Destination=" prefix. An empty destination ("=src") or no
                // '=' falls back to the source-derived destination.
                string explicitDest = null;
                var entry = raw;
                var eq = entry.IndexOf('=');
                if (eq > 0)
                {
                    var destPart = CleanRel(entry.Substring(0, eq));
                    if (destPart.Length > 0) explicitDest = destPart;
                    entry = entry.Substring(eq + 1);
                }

                var cleanRel = entry.Replace('\\', '/').Trim().TrimEnd('/');
                if (cleanRel.Length == 0) continue;
                var localPath = Path.Combine(ProjectDir, cleanRel.Replace('/', Path.DirectorySeparatorChar));

                var destRel = explicitDest ?? CleanRel(cleanRel);
                if (destRel.Length == 0)
                    destRel = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar));

                if (File.Exists(localPath))
                {
                    // For a file, the destination (explicit or derived) is the full target path.
                    Add(outList, localPath, destRel);
                    continue;
                }
                if (!Directory.Exists(localPath))
                {
                    Log.LogWarning("deployPaths: not found {0}", localPath);
                    continue;
                }

                var files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
                if (files.Length == 0)
                {
                    Log.LogWarning("deployPaths: no files under {0}", localPath);
                    continue;
                }
                foreach (var file in files)
                {
                    var relFile = MakeRelative(localPath, file).Replace('\\', '/');
                    Add(outList, file, destRel.Length > 0 ? destRel + "/" + relFile : relFile);
                }
            }

            DeployFiles = outList.ToArray();
            return !Log.HasLoggedErrors;
        }

        private static void Add(List<ITaskItem> list, string source, string isoDest)
        {
            var it = new TaskItem(source);
            // image-relative dest with OS separators for the Copy DestinationFiles template.
            it.SetMetadata("IsoDest", isoDest.Replace('/', Path.DirectorySeparatorChar));
            list.Add(it);
        }

        private static string CleanRel(string s) =>
            string.Join("/", s.Replace('\\', '/').Split('/')
                .Where(seg => seg.Length > 0 && seg != "." && seg != ".."));

        // net472 has no Path.GetRelativePath; derive the relative path via Uri.
        private static string MakeRelative(string baseDir, string full)
        {
            if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                baseDir += Path.DirectorySeparatorChar;
            var rel = new Uri(baseDir).MakeRelativeUri(new Uri(full)).ToString();
            return Uri.UnescapeDataString(rel).Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
