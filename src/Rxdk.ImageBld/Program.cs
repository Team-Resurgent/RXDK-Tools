using Rxdk.XbeImage;

namespace Rxdk.ImageBld;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = ImageBldOptionsParser.Parse(args, expandLegacyArgv: true);

            if (options.ShowUsage)
            {
                ImageBldUsage.Print(Console.Error);
                return 0;
            }

            if (options.Mode == ImageBldParseMode.Dump)
            {
                if (string.IsNullOrWhiteSpace(options.InputFilePath))
                    throw new ImageBldParseException("Missing XBE file path for /DUMP.");

                XbeImageDumper.Dump(options.InputFilePath, Console.Out);
                return 0;
            }

            new XbeImageBuilder().Build(options);
            return 0;
        }
        catch (ImageBldParseException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (XbeImageException ex)
        {
            Console.Error.WriteLine($"IMAGEBLD : error {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"IMAGEBLD : error {ex.Message}");
            return 1;
        }
    }
}
