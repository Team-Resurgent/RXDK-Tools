using System.Runtime.InteropServices;
using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.Managed;
using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Shell;

const string DefaultWirePath = @"E:\SAMPLES";
const string DefaultConsole = "myxbox";

var positional = new List<string>();
var listOnly = false;
var dirsOnly = false;
var transfer = true;
foreach (var arg in args)
{
    if (string.Equals(arg, "--list", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "-l", StringComparison.OrdinalIgnoreCase))
    {
        listOnly = true;
        transfer = false;
        continue;
    }

    if (string.Equals(arg, "--dirs", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "-d", StringComparison.OrdinalIgnoreCase))
    {
        dirsOnly = true;
        transfer = false;
        continue;
    }

    if (string.Equals(arg, "--transfer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "-t", StringComparison.OrdinalIgnoreCase))
    {
        transfer = true;
        continue;
    }

    if (string.Equals(arg, "--recursive", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "-r", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    positional.Add(arg);
}

var wirePath = positional.Count > 0 ? positional[0] : DefaultWirePath;
var consoleName = positional.Count > 1 ? positional[1] : DefaultConsole;

if (transfer)
{
    RunTransfer(consoleName, wirePath);
    return;
}

var recursive = !dirsOnly && (listOnly || positional.Count == 0);
var client = new ManagedXbdmClient();
using var conn = (ManagedXbdmConnection)client.Connect(consoleName);

Console.WriteLine($"Console: {consoleName}");
Console.WriteLine($"Path: {wirePath}");
if (recursive)
    Console.WriteLine("Mode: list (recursive)");
if (dirsOnly)
    Console.WriteLine("Mode: dirs");
Console.WriteLine();

if (dirsOnly)
{
    foreach (var entry in conn.ListDirectory(wirePath))
    {
        var kind = (entry.Attributes & XbdmConstants.AttrDirectory) != 0 ? "dir " : "file";
        Console.WriteLine($"  [{kind}] {entry.Name,-30} {entry.Size,12:N0}");
    }

    return;
}

var files = new List<(string Path, ulong Size)>();
Collect(conn, wirePath, recursive, files);

var totalBytes = files.Aggregate(0UL, (sum, file) => sum + file.Size);
Console.WriteLine($"Files: {files.Count}");
Console.WriteLine($"Total bytes: {totalBytes:N0} ({FormatSize(totalBytes)})");
Console.WriteLine();

foreach (var file in files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).Take(40))
    Console.WriteLine($"  {file.Size,12:N0}  {file.Path}");

if (files.Count > 40)
    Console.WriteLine($"  ... and {files.Count - 40} more");

static void RunTransfer(string consoleName, string wirePath)
{
    var selection = BuildSelection(consoleName, wirePath);
    var (session, catalog) = XboxDragTransferSession.Start(selection);
    using (session)
    {
        var files = catalog.Where(entry => !entry.IsDirectory).ToList();
        var totalBytes = files.Aggregate(0UL, (sum, entry) => sum + entry.Size);

        Console.WriteLine($"Console: {consoleName}");
        Console.WriteLine($"Path: {wirePath}");
        Console.WriteLine($"Transfer test: {files.Count} file(s), {FormatSize(totalBytes)} total");
        Console.WriteLine("Opening progress UI…");
        Console.WriteLine();

        var buffer = new byte[8192];
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        var readPtr = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            var bufferPtr = pin.AddrOfPinnedObject();
            foreach (var entry in files)
            {
                var stream = session.OpenStream(entry);
                if (stream == null)
                    throw new InvalidOperationException($"Could not open '{entry.RelativePath}'.");

                try
                {
                    DrainStream(stream, bufferPtr, buffer.Length, readPtr);
                }
                finally
                {
                    if (stream is IDisposable disposable)
                        disposable.Dispose();
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(readPtr);
            pin.Free();
        }

        Console.WriteLine("Transfer complete.");
    }
}

static void DrainStream(INativeComStream stream, IntPtr bufferPtr, int bufferSize, IntPtr bytesReadPtr)
{
    while (true)
    {
        var hr = stream.Read(bufferPtr, bufferSize, bytesReadPtr);
        var read = Marshal.ReadInt32(bytesReadPtr);
        if (read == 0)
            return;

        if (hr < 0)
            throw new InvalidOperationException($"Stream read failed: 0x{hr:X8}");
    }
}

static FileSelection BuildSelection(string consoleName, string wirePath)
{
    var name = wirePath.TrimEnd('\\');
    var slash = name.LastIndexOf('\\');
    if (slash >= 0)
        name = name[(slash + 1)..];

    return new FileSelection
    {
        ConsoleName = consoleName,
        FolderDisplayPath = $"{consoleName}\\{wirePath.TrimEnd('\\')}",
        Items =
        [
            new FileSelectionItem
            {
                Name = name,
                WirePath = wirePath,
                IsDirectory = true,
            },
        ],
    };
}

static void Collect(ManagedXbdmConnection conn, string wirePath, bool recursive, List<(string, ulong)> files)
{
    foreach (var entry in conn.ListDirectory(wirePath))
    {
        var childWire = $"{wirePath.TrimEnd('\\')}\\{entry.Name}";
        if ((entry.Attributes & XbdmConstants.AttrDirectory) != 0)
        {
            if (recursive)
                Collect(conn, childWire, true, files);
            continue;
        }

        ulong size;
        try
        {
            size = conn.GetFileAttributes(childWire).Size;
        }
        catch
        {
            size = entry.Size;
        }

        files.Add((childWire, size));
    }
}

static string FormatSize(ulong bytes)
{
    string[] units = ["B", "KB", "MB", "GB"];
    var value = (double)bytes;
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1)
    {
        value /= 1024;
        unit++;
    }

    return $"{value:0.##} {units[unit]}";
}
