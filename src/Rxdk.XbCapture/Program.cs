using System.CommandLine;
using Rxdk.XbFile;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbCapture;

/// <summary>
/// xbcapture: save the image on the Xbox's front buffer. The XDK tool wrote only .bmp; this one
/// writes .png, .jpg/.jpeg or .bmp, chosen by the file's extension.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var image = new Argument<string>("image")
        {
            Description = $"Image file to write ({string.Join(", ", ScreenshotImage.SupportedExtensions)}; the extension picks the format).",
        };

        var xbox = new Option<string?>(["-x", "/x", "--xbox"])
        {
            Description = "Xbox hostname or IP address.",
        };

        var root = new RootCommand("Captures the current image on the Xbox's front buffer and saves it as the specified image file.")
        {
            image, xbox,
        };

        var exitCode = 0;
        root.SetHandler((string path, string? target) => exitCode = Execute(path, target), image, xbox);

        var parseExit = await root.InvokeAsync(args);
        return parseExit != 0 ? parseExit : exitCode;
    }

    private static int Execute(string path, string? xbox)
    {
        try
        {
            // Check the file type before connecting, so a typo costs nothing on the kit.
            ScreenshotImage.FormatFromPath(path);
            var fullPath = Path.GetFullPath(path);

            using var session = XbConsoleSession.Connect(xbox);
            session.Connection.CaptureScreenshot(fullPath);
            Console.WriteLine($"Saved {fullPath}");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message.Split(" (Parameter")[0]}");
            return 1;
        }
        catch (XbdmException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
        catch (XbFileException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
}
