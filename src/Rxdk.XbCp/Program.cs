using System.CommandLine;
using System.CommandLine.Invocation;
using Rxdk.XbFile;

namespace Rxdk.XbCp;

internal static class Program
{
    private static readonly Option<string?> XboxOption = new(["-x", "/x", "--xbox"])
    {
        Description = "Xbox hostname or IP address.",
    };

    private static readonly Option<bool> QuietOption = new(["/q", "-q"]) { Description = "Suppress file list output." };
    private static readonly Option<bool> YesOption = new(["/y", "-y"]) { Description = "Overwrite without prompting." };
    private static readonly Option<bool> ForceOption = new(["/f", "-f"]) { Description = "Force overwrite read-only files." };
    private static readonly Option<bool> NewerOption = new(["/d", "-d"]) { Description = "Copy only if source is newer." };
    private static readonly Option<bool> EmptyOption = new(["/e", "-e"]) { Description = "Skip empty directories." };
    private static readonly Option<bool> CreateDestOption = new(["/t", "-t"]) { Description = "Create destination directory if missing." };
    private static readonly Option<bool> RecursiveOption = new(["/r", "-r"]) { Description = "Recursive copy." };
    private static readonly Option<bool> SubdirsOption = new(["/s", "-s"]) { Description = "Search subdirectories for wildcards." };
    private static readonly Option<bool> HiddenOption = new(["/h", "-h"]) { Description = "Include hidden files." };

    private static readonly Argument<string[]> PathsArgument = new("paths")
    {
        Description = "Source file(s) and destination (last path).",
        Arity = ArgumentArity.OneOrMore,
    };

    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("Copy files to and from the Xbox target system.")
        {
            PathsArgument,
            XboxOption,
            QuietOption,
            YesOption,
            ForceOption,
            NewerOption,
            EmptyOption,
            CreateDestOption,
            RecursiveOption,
            SubdirsOption,
            HiddenOption,
        };

        root.SetHandler(Execute);

        try
        {
            return await root.InvokeAsync(XbLegacyArgv.ForXbCp(args));
        }
        catch (XbFileException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static void Execute(InvocationContext context)
    {
        var parse = context.ParseResult;
        try
        {
            var paths = parse.GetValueForArgument(PathsArgument) ?? Array.Empty<string>();
            if (paths.Length < 1)
                throw new XbFileException("At least one source and a destination are required.");

            var parsed = paths.Select(XbPath.Parse).ToList();
            var dest = parsed[^1];
            var sources = parsed.Take(parsed.Count - 1).ToList();
            if (sources.Count == 0)
                sources.Add(XbPath.Parse("."));

            var xbox = parse.GetValueForOption(XboxOption);
            using var session = XbConsoleSession.PathNeedsXbox(parsed)
                ? XbConsoleSession.Connect(xbox)
                : null;

            var options = new XbCopyOptions
            {
                Quiet = parse.GetValueForOption(QuietOption),
                ForceReplace = parse.GetValueForOption(YesOption),
                ForceReadOnly = parse.GetValueForOption(ForceOption),
                CopyIfNewer = parse.GetValueForOption(NewerOption),
                SkipEmptyDirs = parse.GetValueForOption(EmptyOption),
                EnsureDestDir = parse.GetValueForOption(CreateDestOption),
            };
            options.Recurse.Recursive = parse.GetValueForOption(RecursiveOption);
            options.Recurse.SearchSubdirs = parse.GetValueForOption(SubdirsOption);
            options.Recurse.IncludeHidden = parse.GetValueForOption(HiddenOption);

            context.ExitCode = new XbCopyService(options, session).Execute(sources, dest) ? 0 : 1;
        }
        catch (XbFileException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            context.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            context.ExitCode = 1;
        }
    }
}
