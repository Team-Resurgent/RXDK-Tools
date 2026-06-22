using Rxdk.XbeImage;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var outputDir = Path.Combine(repoRoot, "out");
Directory.CreateDirectory(outputDir);

var options = new ImageBldOptions
{
    InputFilePath = Path.Combine(repoRoot, "src", "TestFiles", "TriangleXDK.exe"),
    OutputFilePath = Path.Combine(outputDir, "managed_triangle.xbe"),
    NoWarnLibraryApproval = true,
};

try
{
    new XbeImageBuilder().Build(options);
    Console.WriteLine("OK");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex}");
}
