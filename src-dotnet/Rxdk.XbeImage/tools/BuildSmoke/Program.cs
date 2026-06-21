using Rxdk.XbeImage;

var options = new ImageBldOptions
{
    InputFilePath = @"d:\Git\XboxNeighborhood\src-dotnet\TestFiles\TriangleXDK.exe",
    OutputFilePath = @"d:\Git\XboxNeighborhood\artifacts\managed_triangle.xbe",
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
