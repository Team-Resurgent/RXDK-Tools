using Rxdk.Engine.Bootstrap;
using Xunit;

namespace Rxdk.Engine.Tests;

/// <summary>
/// Which files of the RXDK-Tools release zip the installer writes, and where: everything in its
/// tools/ folder, keeping subfolders such as the CMake toolchain's cmake/Platform/.
/// </summary>
public sealed class HostToolsInstallerTests
{
    [Theory]
    [InlineData("rxdk-managed-win-x64/tools/Rxdk.Cli.exe", "Rxdk.Cli.exe")]
    [InlineData("dist/rxdk-managed-linux-x64/tools/imagebld", "imagebld")]
    [InlineData("tools/xbcp.exe", "xbcp.exe")]
    [InlineData("rxdk-managed-win-x64/tools/cmake/RxdkToolchain.cmake", "cmake/RxdkToolchain.cmake")]
    [InlineData("rxdk-managed-win-x64/tools/cmake/Platform/XboxOG.cmake", "cmake/Platform/XboxOG.cmake")]
    [InlineData(@"rxdk-managed-win-x64\tools\cmake\RxdkTitle.cmake", "cmake/RxdkTitle.cmake")]
    [InlineData("rxdk-managed-win-x64/install-dotnet-runtime.ps1", null)]
    [InlineData("rxdk-managed-win-x64/runtime/dotnet-runtime.exe", null)]
    public void ToolsArchiveTarget(string entry, string? expected)
    {
        Assert.Equal(expected, HostToolsInstaller.ToolsArchiveTarget(entry));
    }

    [Fact]
    public void ResolveTarget_KeepsSubfoldersUnderTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rxdk-tools-root");
        Assert.Equal(
            Path.Combine(root, "cmake", "Platform", "XboxOG.cmake"),
            HostToolsInstaller.ResolveTarget(root, "cmake/Platform/XboxOG.cmake"));
    }

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("cmake/../../evil.exe")]
    public void ResolveTarget_RefusesEntriesOutsideTheRoot(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), "rxdk-tools-root");
        Assert.Throws<InvalidOperationException>(() => HostToolsInstaller.ResolveTarget(root, relative));
    }
}
