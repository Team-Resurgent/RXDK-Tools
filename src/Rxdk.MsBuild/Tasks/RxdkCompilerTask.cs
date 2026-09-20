using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Rxdk.MsBuild.Tasks
{
    /// <summary>
    /// Shared base for the RXDK compiler/linker/archiver tasks. Resolves the RXDK LLVM toolchain
    /// (the Team-Resurgent clang/lld/llvm-ar fork for the xboxog target) and exposes the common
    /// target triple + the tool paths. Mirrors Rxdk.Engine's LlvmRuntime — kept separate because
    /// this MSBuild task assembly targets net472 and cannot reference the net8 engine.
    /// </summary>
    public abstract class RxdkCompilerTask : RxdkToolTask
    {
        [Required]
        public virtual ITaskItem[] Sources { get; set; }

        /// <summary>The clang target triple for the original Xbox (32-bit x86, PE/COFF, GNU ABI).</summary>
        public string Target => "i686-pc-windows-gnu";
        public string Machine => "-march=pentium3";

        private static string Exe(string name) => name + ".exe";

        private static string ArchiveDirName
        {
            get
            {
                var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
                return "xboxog-windows-" + arch; // MSBuild/VS is Windows-only
            }
        }

        /// <summary>
        /// Resolve the LLVM toolchain root (the dir with bin/clang), or null (logging an error).
        /// RXDK_LLVM is an OVERRIDE (CI / non-standard install); the normal case is the managed
        /// install the RXDK tool window's Complete Setup put at %LocalAppData%\RXDK\llvm.
        /// </summary>
        protected string ResolveLlvmRoot()
        {
            var env = Environment.GetEnvironmentVariable("RXDK_LLVM");
            if (!string.IsNullOrEmpty(env))
            {
                env = env.Trim();
                if (File.Exists(Path.Combine(env, "bin", Exe("clang"))))
                    return env;
                Log.LogError("RXDK_LLVM has no bin/clang: {0}", env);
                return null;
            }
            foreach (var root in ManagedLlvmCandidates())
                if (File.Exists(Path.Combine(root, "bin", Exe("clang"))))
                    return root;
            Log.LogError("RXDK LLVM toolchain not installed. Open the RXDK tool window and run " +
                         "Complete Setup (or set RXDK_LLVM to an unpacked xboxog-<os>-<arch> root).");
            return null;
        }

        private static IEnumerable<string> ManagedLlvmCandidates()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RXDK", "llvm");
            yield return Path.Combine(root, ArchiveDirName);
            yield return root; // in case the archive was unpacked flat
        }

        protected static string ClangExe(string root) => Path.Combine(root, "bin", Exe("clang"));
        protected static string ClangxxExe(string root)
        {
            var cxx = Path.Combine(root, "bin", Exe("clang++"));
            return File.Exists(cxx) ? cxx : ClangExe(root);
        }
        protected static string ArExe(string root)
        {
            var ar = Path.Combine(root, "bin", Exe("llvm-ar"));
            return File.Exists(ar) ? ar : Path.Combine(root, "bin", Exe("llvm-lib"));
        }

        /// <summary>The versioned clang resource include (&lt;root&gt;/lib/clang/&lt;ver&gt;/include),
        /// re-added via -isystem because the -nostdinc build otherwise can't find stddef.h/stdarg.h.</summary>
        protected static string ResourceInclude(string root)
        {
            var clangLib = Path.Combine(root, "lib", "clang");
            if (!Directory.Exists(clangLib)) return null;
            foreach (var ver in Directory.GetDirectories(clangLib))
            {
                var inc = Path.Combine(ver, "include");
                if (Directory.Exists(inc)) return inc;
            }
            return null;
        }

        /// <summary>The i386 compiler-rt builtins archive to append to a title link (the -nostdlib
        /// clang link ships none), or null.</summary>
        protected static string BuiltinsArchive(string root)
        {
            var clangLib = Path.Combine(root, "lib", "clang");
            if (Directory.Exists(clangLib))
                foreach (var ver in Directory.GetDirectories(clangLib))
                {
                    foreach (var dir in new[] { Path.Combine(ver, "lib", "windows"), Path.Combine(ver, "lib") })
                        foreach (var name in new[] { "libclang_rt.builtins-i386.a", "clang_rt.builtins-i386.lib" })
                        {
                            var p = Path.Combine(dir, name);
                            if (File.Exists(p)) return p;
                        }
                }
            return null;
        }
    }
}
