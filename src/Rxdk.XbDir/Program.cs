using System.CommandLine;
using System.CommandLine.Invocation;
using Rxdk.XbFile;

namespace Rxdk.XbDir;

internal static class Program
{
    private static readonly Option<string?> XboxOption = new(["-x", "/x", "--xbox"])
    {
        Description = "Xbox hostname or IP address.",
    };

    private static readonly Option<bool> BareOption = new(["/b", "-b"]) { Description = "Bare format (names only)." };
    private static readonly Option<bool> WideOption = new(["/w", "-w"]) { Description = "Wide column format." };
    private static readonly Option<bool> RecursiveOption = new(["/r", "-r"]) { Description = "Recursive listing." };
    private static readonly Option<bool> SubdirsOption = new(["/s", "-s"]) { Description = "Search subdirectories." };
    private static readonly Option<bool> HiddenOption = new(["/h", "-h"]) { Description = "Include hidden files." };
    private static readonly Option<bool> SortNameOption = new(["/on", "-on"]) { Description = "Sort by name." };
    private static readonly Option<bool> SortDateOption = new(["/od", "-od"]) { Description = "Sort by date." };
    private static readonly Option<bool> SortSizeOption = new(["/os", "-os"]) { Description = "Sort by size." };
    private static readonly Option<bool> SortNameRevOption = new(["/o-n", "-o-n"]) { Description = "Sort by name (reverse)." };
    private static readonly Option<bool> SortDateRevOption = new(["/o-d", "-o-d"]) { Description = "Sort by date (reverse)." };
    private static readonly Option<bool> SortSizeRevOption = new(["/o-s", "-o-s"]) { Description = "Sort by size (reverse)." };

    private static readonly Argument<string[]> PathsArgument = new("paths")
    {
        Description = "Files or directories to list.",
        Arity = ArgumentArity.ZeroOrMore,
    };

    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("List files on the PC or Xbox target system.")
        {
            PathsArgument,
            XboxOption,
            BareOption,
            WideOption,
            RecursiveOption,
            SubdirsOption,
            HiddenOption,
            SortNameOption,
            SortDateOption,
            SortSizeOption,
            SortNameRevOption,
            SortDateRevOption,
            SortSizeRevOption,
        };

        root.SetHandler(Execute);

        try
        {
            return await root.InvokeAsync(XbLegacyArgv.ForXbDir(args));
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
            var parsed = paths.Select(XbPath.Parse).ToList();
            var xbox = parse.GetValueForOption(XboxOption);

            using var session = parsed.Count == 0 || parsed.Any(p => p.IsXbox) || !string.IsNullOrWhiteSpace(xbox)
                ? XbConsoleSession.Connect(xbox)
                : null;

            var options = new XbDirOptions
            {
                Bare = parse.GetValueForOption(BareOption),
                Wide = parse.GetValueForOption(WideOption),
            };
            options.Recurse.Recursive = parse.GetValueForOption(RecursiveOption);
            options.Recurse.SearchSubdirs = parse.GetValueForOption(SubdirsOption);
            options.Recurse.IncludeHidden = parse.GetValueForOption(HiddenOption);

            if (parse.GetValueForOption(SortNameRevOption))
            {
                options.Sort = XbDirSort.Name;
                options.ReverseSort = true;
            }
            else if (parse.GetValueForOption(SortDateRevOption))
            {
                options.Sort = XbDirSort.Date;
                options.ReverseSort = true;
            }
            else if (parse.GetValueForOption(SortSizeRevOption))
            {
                options.Sort = XbDirSort.Size;
                options.ReverseSort = true;
            }
            else if (parse.GetValueForOption(SortNameOption))
                options.Sort = XbDirSort.Name;
            else if (parse.GetValueForOption(SortDateOption))
                options.Sort = XbDirSort.Date;
            else if (parse.GetValueForOption(SortSizeOption))
                options.Sort = XbDirSort.Size;

            context.ExitCode = new XbDirService(options, session).Execute(parsed) ? 0 : 1;
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
