using System.Text.Json.Serialization;

namespace Rxdk.Engine.Model;

// C# port of RXDK-VSCode src/projectTypes.ts. The rxdk.project.json manifest is the
// stable contract shared with the VS Code extension, so field names and semantics here
// must match that file exactly. JSON is camelCase to match the on-disk manifests.

/// <summary>Output kind. Omitted = Executable.</summary>
public enum RxdkProjectKind
{
    Executable,
    Library,
    Dxt,
}

/// <summary>
/// Which prebuilt SDK library variant this project links. The staged SDK ships every
/// library in both flavors side by side (lib/debug: Debug -O0 -g, lib/release:
/// ReleaseSmall -Os). Omitted = Release.
/// </summary>
public enum RxdkConfiguration
{
    Debug,
    Release,
}

/// <summary>A file embedded into the XBE at build time (imagebld /insertfile).</summary>
public sealed class RxdkEmbedFile
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Options passed to imagebld (/stack, /debug, /limitmem, …). Omitted keys use RXDK defaults.</summary>
public sealed class RxdkImageBuildOptions
{
    public int? StackSize { get; set; }
    public bool? Debug { get; set; }
    public bool? NoLogo { get; set; }
    public bool? NoLibWarn { get; set; }
    public bool? LimitMemory { get; set; }
    public bool? DontModifyHardDisk { get; set; }
    public bool? DontMountUtilityDrive { get; set; }
    public bool? FormatUtilityDrive { get; set; }

    /// <summary>16384, 32768, or 65536 bytes. Omit or 0 for imagebld default.</summary>
    public int? UtilityDriveClusterSize { get; set; }

    /// <summary>Section names for /nopreload:&lt;section&gt;.</summary>
    public List<string>? NoPreload { get; set; }

    // ---- XBE certificate (imagebld /TEST* switches) ----

    /// <summary>Title ID (/TESTID). Decimal or 0x-hex string.</summary>
    public string? TestId { get; set; }
    /// <summary>Alternate title ID, optionally "number,key" (/TESTALTID).</summary>
    public string? TestAltId { get; set; }
    /// <summary>Allowed regions bitmask (/TESTREGION).</summary>
    public string? TestRegion { get; set; }
    /// <summary>Ratings value (/TESTRATINGS).</summary>
    public string? TestRatings { get; set; }
    /// <summary>Allowed media types bitmask (/TESTMEDIATYPES).</summary>
    public string? TestMediaTypes { get; set; }
    /// <summary>LAN key (/TESTLANKEY).</summary>
    public string? TestLanKey { get; set; }
    /// <summary>Signature key (/TESTSIGNKEY).</summary>
    public string? TestSignKey { get; set; }

    // ---- Title info (imagebld title switches) ----

    /// <summary>Test title name (/TESTNAME).</summary>
    public string? TestName { get; set; }
    /// <summary>Test version number (/TESTVERSION).</summary>
    public string? TestVersion { get; set; }
    /// <summary>Project-relative title info file (/TITLEINFO).</summary>
    public string? TitleInfo { get; set; }
    /// <summary>Project-relative title image, XPR format (/TITLEIMAGE).</summary>
    public string? TitleImage { get; set; }
    /// <summary>Project-relative default save image, XPR format (/DEFAULTSAVEIMAGE).</summary>
    public string? DefaultSaveImage { get; set; }
}

public sealed class RxdkProjectManifest
{
    public string Name { get; set; } = "";

    /// <summary>Output kind. Omitted = Executable.</summary>
    public RxdkProjectKind? Type { get; set; }

    /// <summary>Which SDK library variant to link (lib/debug or lib/release). Omitted = Release.</summary>
    public RxdkConfiguration? Configuration { get; set; }

    public List<string>? Sources { get; set; }
    public List<string>? Libraries { get; set; }

    /// <summary>
    /// Project-relative paths to .rdf resource-description files compiled by the bundler tool
    /// before the C/C++ sources (each produces a Resource.h consumed at compile time and a
    /// packed .xpr loaded at runtime, written to the paths named inside the .rdf). Omitted =
    /// auto-discover every *.rdf under the project root.
    /// </summary>
    public List<string>? Resources { get; set; }

    /// <summary>Extra directories to resolve <see cref="Libraries"/> names from, searched after
    /// the SDK lib dir (project-relative or absolute). For linking your own prebuilt libs by name.</summary>
    public List<string>? LibraryPaths { get; set; }

    /// <summary>Explicit prebuilt static-library files linked verbatim (project-relative or absolute
    /// .lib paths), in addition to the named <see cref="Libraries"/>.</summary>
    public List<string>? AdditionalLibraries { get; set; }

    /// <summary>
    /// Project-relative paths to library projects (folders containing an rxdk.project.json with
    /// type:"library") this project links. Resolved transitively, built in dependency order to
    /// static .libs, then linked. Their PublicIncludePaths are added to this project's compile
    /// include path automatically.
    /// </summary>
    public List<string>? ProjectReferences { get; set; }

    public string? OutputDir { get; set; }

    /// <summary>Project-relative directories copied recursively on deploy (e.g. "media" -> xe:\\&lt;name&gt;\\media).</summary>
    public List<string>? DeployPaths { get; set; }

    /// <summary>Files embedded into the XBE at build time (imagebld /insertfile).</summary>
    public List<RxdkEmbedFile>? Embed { get; set; }

    /// <summary>Pack the build output into an .iso via xdvdfs (default true). When false the build
    /// stops at the .xbe (plus any deployPaths staged into out\Build), skipping ISO creation.</summary>
    public bool? CreateIso { get; set; }

    /// <summary>Force every file to be (re)copied on deploy. Default false: deploy passes xbcp -d so
    /// only files that are new or newer than the console copy are sent (fast incremental redeploy).
    /// Set true to always overwrite regardless of timestamp.</summary>
    public bool? ForceCopy { get; set; }

    /// <summary>imagebld.exe switches for the PE -> XBE step.</summary>
    public RxdkImageBuildOptions? ImageBuild { get; set; }

    /// <summary>Extra project-relative include directories (passed as cl /I after sdk/include).</summary>
    public List<string>? IncludePaths { get; set; }

    /// <summary>
    /// Include directories a library project exports to referencing projects (added to their
    /// compile include path). For an executable this behaves like an extra local include path.
    /// </summary>
    public List<string>? PublicIncludePaths { get; set; }

    /// <summary>Extra preprocessor defines (cl /D), appended after RXDK defaults.</summary>
    public List<string>? Defines { get; set; }

    /// <summary>Extra raw compiler flags, appended after the RXDK defaults so a project can
    /// override them (e.g. "-mno-ms-bitfields" to counteract the default MSVC bitfield layout).
    /// Passed verbatim to the C/C++ compile of every source in this project.</summary>
    public List<string>? CompileFlags { get; set; }

    /// <summary>
    /// C++ language standard for this project's C++ sources, e.g. "c++17". Omitted = the RXDK
    /// default (see <see cref="EffectiveCppStandard"/>).
    ///
    /// XDK-era C++ predates most of what the default standard removed. Some of it can be opted
    /// back in per feature (std::auto_ptr has a libc++ macro, and RXDK always passes that one),
    /// but the C++98 allocator members -- rebind, pointer, address, two-argument allocate --
    /// have no such escape: libc++ gates them on the standard alone. A project that uses them
    /// has to be compiled as the C++ it was written in.
    /// </summary>
    public string? CppStandard { get; set; }

    /// <summary>
    /// Compile C++ sources with exception support (-fexceptions). Omitted = true (on): RXDK's
    /// C++ runtime supports real DWARF/Itanium exceptions on the console, so titles get them by
    /// default. Set to false for -fno-exceptions when a title wants to shave the EH tables on a
    /// 64 MB console and never throws.
    ///
    /// The runtime side is in place either way: libcpp.lib bundles libunwind, which finds the
    /// merged .eh_frame between the markers the link places (and checks) around it. This flag
    /// only decides whether the title's own code may throw.
    /// </summary>
    public bool? Exceptions { get; set; }

    /// <summary>
    /// Incremental compilation. Omitted = true (on): a source is recompiled only when its object is
    /// missing or older than the source or any header it includes (tracked via clang <c>-MD</c>
    /// depfiles), and the link / imagebld / ISO steps are skipped when nothing recompiled and the
    /// outputs are still newer than every object, linked library and the manifest. Set false to
    /// force a full recompile + relink on every build.
    /// </summary>
    public bool? Incremental { get; set; }

    /// <summary>
    /// Per-configuration overrides keyed by config name (e.g. "Debug", "Release"). When present the
    /// build resolves one configuration via <see cref="ResolveConfiguration"/>: the chosen config's
    /// fields win, falling back to this (top-level) manifest for anything the config omits. A flat
    /// manifest with no <c>configurations</c> is treated as a single implicit configuration, so
    /// existing single-config manifests keep working unchanged.
    /// </summary>
    public Dictionary<string, RxdkProjectManifest>? Configurations { get; set; }

    /// <summary>Configuration to build when the caller doesn't name one (else the first key).</summary>
    public string? DefaultConfiguration { get; set; }

    // ---- Multi-configuration resolution ----

    /// <summary>
    /// Collapse a (possibly multi-config) manifest to a single effective manifest for
    /// <paramref name="configName"/>. Flat manifests return themselves. Otherwise the named config
    /// (or <see cref="DefaultConfiguration"/>, or the first key) is merged over the shared
    /// top-level fields. Config-name match is case-insensitive.
    /// </summary>
    public RxdkProjectManifest ResolveConfiguration(string? configName = null)
    {
        if (Configurations is null || Configurations.Count == 0)
            return this;

        string Pick()
        {
            foreach (var want in new[] { configName, DefaultConfiguration })
                if (!string.IsNullOrEmpty(want))
                    foreach (var k in Configurations.Keys)
                        if (string.Equals(k, want, StringComparison.OrdinalIgnoreCase))
                            return k;
            return Configurations.Keys.First();
        }

        var over = Configurations[Pick()];
        // Field-wise override: the config value wins, the shared top-level fills the gaps.
        return new RxdkProjectManifest
        {
            Name = string.IsNullOrEmpty(over.Name) ? Name : over.Name,
            Type = over.Type ?? Type,
            Configuration = over.Configuration ?? Configuration,
            Sources = over.Sources ?? Sources,
            Libraries = over.Libraries ?? Libraries,
            Resources = over.Resources ?? Resources,
            LibraryPaths = over.LibraryPaths ?? LibraryPaths,
            AdditionalLibraries = over.AdditionalLibraries ?? AdditionalLibraries,
            ProjectReferences = over.ProjectReferences ?? ProjectReferences,
            OutputDir = over.OutputDir ?? OutputDir,
            DeployPaths = over.DeployPaths ?? DeployPaths,
            Embed = over.Embed ?? Embed,
            CreateIso = over.CreateIso ?? CreateIso,
            ForceCopy = over.ForceCopy ?? ForceCopy,
            ImageBuild = over.ImageBuild ?? ImageBuild,
            IncludePaths = over.IncludePaths ?? IncludePaths,
            PublicIncludePaths = over.PublicIncludePaths ?? PublicIncludePaths,
            Defines = over.Defines ?? Defines,
            CompileFlags = over.CompileFlags ?? CompileFlags,
            CppStandard = over.CppStandard ?? CppStandard,
            Exceptions = over.Exceptions ?? Exceptions,
            Incremental = over.Incremental ?? Incremental,
            // Resolved: no nested configurations remain.
            Configurations = null,
            DefaultConfiguration = null,
        };
    }

    /// <summary>Configuration names this manifest offers (empty for a flat single-config manifest).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ConfigurationNames =>
        Configurations is null ? System.Array.Empty<string>() : Configurations.Keys.ToList();

    // ---- Derived helpers (port of the free functions in projectTypes.ts) ----

    [JsonIgnore]
    public RxdkProjectKind EffectiveType => Type ?? RxdkProjectKind.Executable;

    [JsonIgnore]
    public RxdkConfiguration EffectiveConfiguration => Configuration ?? RxdkConfiguration.Release;

    /// <summary>The C++ standard to compile with; "c++23" unless the project asks for another.</summary>
    [JsonIgnore]
    public string EffectiveCppStandard =>
        string.IsNullOrWhiteSpace(CppStandard) ? "c++23" : CppStandard!.Trim();

    [JsonIgnore]
    public bool IsLibrary => Type == RxdkProjectKind.Library;

    /// <summary>
    /// True for a DXT (debug-monitor extension) project. Builds a raw flat .dxt (entry
    /// DxtEntry, via imagebld /DXT) instead of an XBE; deploys to xe:\dxt and loads on a warm
    /// reboot. Not debuggable via attach (it runs inside the debug monitor).
    /// </summary>
    [JsonIgnore]
    public bool IsDxt => Type == RxdkProjectKind.Dxt;

    [JsonIgnore]
    public bool UsesCpp =>
        Sources?.Any(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"\.(cpp|cxx|cc)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ?? false;

    /// <summary>True when the project has compilable sources that need C/C++ IntelliSense.</summary>
    [JsonIgnore]
    public bool NeedsIntelliSense =>
        Sources?.Any(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"\.(c|cpp|cxx|cc|h|hpp)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ?? false;
}
