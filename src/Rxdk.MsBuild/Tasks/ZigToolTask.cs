using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Shared base for the zig-driven tasks (cc / ar). Resolves the pinned Zig executable and
    /// exposes the common Target / Machine flags. Subclasses override <see cref="SubTool"/>
    /// ("cc" or "ar") and <see cref="Execute"/> to build their explicit argument lists.
    /// </summary>
    public abstract class ZigToolTask : RxdkToolTask
    {
        // Pinned Zig version the SDK is built/tested against (mirrors Rxdk.Engine ZigRuntime.ZigVersion).
        private const string ZigVersion = "0.16.0";

        [Required]
        public virtual ITaskItem[] Sources { get; set; }

        /// <summary>The zig sub-command ("cc" or "ar"); MUST be the first token on the real
        /// command line so the tool processes the response file that follows.</summary>
        public abstract string SubTool { get; }

        public string Target => "x86-windows-gnu";
        public string Machine => "-march=pentium3";

        /// <summary>
        /// Resolve zig.exe. RXDK_ZIG is only an OVERRIDE (CI, a non-standard install). The
        /// normal case is a bare VSIX install whose "Complete Setup" put the pinned Zig at the
        /// managed default location with no env var set -- so fall back to it exactly like
        /// Rxdk.Engine's ZigRuntime does. Returns null (and logs an error) when not found.
        /// </summary>
        protected string ResolveZig()
        {
            var zig = Environment.GetEnvironmentVariable("RXDK_ZIG");
            if (!string.IsNullOrEmpty(zig))
            {
                zig = zig.Trim();
                if (File.Exists(zig))
                    return zig;
                Log.LogError($"RXDK_ZIG points to a missing file: {zig}");
                return null;
            }

            foreach (var candidate in ManagedZigCandidates())
                if (File.Exists(candidate))
                    return candidate;

            Log.LogError("Zig is not installed. Open the RXDK tool window and run Complete Setup (or set RXDK_ZIG).");
            return null;
        }

        // The managed pinned-Zig install, Windows layout (mirrors Rxdk.Engine ZigRuntime /
        // RxdkPaths.GetZigInstallRoot): %LocalAppData%\RXDK\zig\<ver>\zig-x86_64-windows-<ver>\zig.exe.
        private static IEnumerable<string> ManagedZigCandidates()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RXDK", "zig");
            yield return Path.Combine(root, ZigVersion, $"zig-x86_64-windows-{ZigVersion}", "zig.exe");
            yield return Path.Combine(root, ZigVersion, "zig.exe");
        }
    }
}
