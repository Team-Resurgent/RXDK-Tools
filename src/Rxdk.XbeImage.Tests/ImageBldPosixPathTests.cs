using Rxdk.XbeImage;
using Xunit;

namespace Rxdk.XbeImage.Tests;

/// <summary>
/// POSIX absolute paths (Linux/macOS) must not be mistaken for legacy '/'-switches.
/// Regression for the imagebld-on-Linux failure where a positional "/home/x" input
/// threw "Unrecognized option" because any leading '/' was treated as a switch.
/// </summary>
public sealed class ImageBldPosixPathTests
{
    [Theory]
    [InlineData("/home/user/app.exe", true)]   // POSIX path: '/' before any ':'
    [InlineData("/tmp/out.dxt", true)]
    [InlineData("/a/b", true)]
    [InlineData("/DXT", false)]                 // plain switch: no second '/'
    [InlineData("/nologo", false)]
    [InlineData("/in:/home/user/app.exe", false)] // colon-value switch: ':' before '/'
    [InlineData("/out:/tmp/app.xbe", false)]
    [InlineData("/stack:262144", false)]
    [InlineData("/TESTID:0xffff0001", false)]
    [InlineData("/in:C:\\build\\app.exe", false)] // Windows value: no '/' at all
    public void LooksLikePosixPath_classifies(string arg, bool expected)
    {
        Assert.Equal(expected, ImageBldLegacyArgv.LooksLikePosixPath(arg));
    }

    [Fact]
    public void Dxt_mode_accepts_positional_posix_paths()
    {
        var options = ImageBldOptionsParser.Parse(
            new[] { "/DXT", "/home/u/in.exe", "/home/u/out.dxt" }, expandLegacyArgv: true);

        Assert.Equal(ImageBldParseMode.Dxt, options.Mode);
        Assert.Equal("/home/u/in.exe", options.InputFilePath);
        Assert.Equal("/home/u/out.dxt", options.OutputFilePath);
    }

    [Fact]
    public void Dxt_mode_accepts_colon_attached_posix_paths()
    {
        var options = ImageBldOptionsParser.Parse(
            new[] { "/DXT", "/in:/home/u/in.exe", "/out:/home/u/out.dxt" }, expandLegacyArgv: true);

        Assert.Equal(ImageBldParseMode.Dxt, options.Mode);
        Assert.Equal("/home/u/in.exe", options.InputFilePath);
        Assert.Equal("/home/u/out.dxt", options.OutputFilePath);
    }

    [Fact]
    public void Build_mode_accepts_colon_attached_posix_paths()
    {
        var options = ImageBldOptionsParser.Parse(
            new[] { "/in:/home/u/app.exe", "/out:/home/u/app.xbe", "/nologo" }, expandLegacyArgv: true);

        Assert.Equal("/home/u/app.exe", options.InputFilePath);
        Assert.Equal("/home/u/app.xbe", options.OutputFilePath);
    }

    [Fact]
    public void Dump_mode_accepts_positional_posix_path()
    {
        var options = ImageBldOptionsParser.Parse(
            new[] { "/DUMP", "/home/u/app.xbe" }, expandLegacyArgv: true);

        Assert.Equal(ImageBldParseMode.Dump, options.Mode);
        Assert.Equal("/home/u/app.xbe", options.InputFilePath);
    }
}
