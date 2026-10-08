using Rxdk.Engine.Build;
using Xunit;

namespace Rxdk.Engine.Tests;

/// <summary>
/// XboxTarget.CodegenFlags is the original Xbox ABI every engine compile shares (titles, SDK
/// libraries, generated assembly). Dropping a flag would build objects that disagree with the
/// shipped SDK libraries, so the set is pinned here.
/// </summary>
public sealed class XboxTargetTests
{
    [Fact]
    public void CodegenFlags_TargetThePentiumIII()
    {
        Assert.Contains("-march=pentium3", XboxTarget.CodegenFlags);
    }

    [Fact]
    public void CodegenFlags_MakeLongDoubleTheDoubleType()
    {
        // The XDK compiler, the msvc-ABI SDK libraries and the Xbox 360 all use a 64-bit
        // long double; clang's i686-pc-windows-gnu default is the 80-bit x87 type.
        Assert.Contains("-mlong-double-64", XboxTarget.CodegenFlags);
    }
}
