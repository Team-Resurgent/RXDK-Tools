using Rxdk.Xbdm;
using Xunit;

namespace Rxdk.XboxLaunch.Tests;

/// <summary>
/// xbox-launch waits for the title's module load before taking a thread-create or break as the
/// title's initial stop. XBDM names the module after the XBE's PE image, so LEGO Island's
/// default.xbe loads as isle.exe; matching on "default" never fired and every such launch timed
/// out, leaving the title stopped.
/// </summary>
public sealed class LaunchTitleModuleTests
{
    private static XbdmModLoadNotification Mod(string name, uint flags = 0) =>
        new(name, 0x11000, 0x100000, 0, 0, flags);

    [Fact]
    public void XbeFlaggedModule_IsTheTitle_WhateverItsName()
    {
        Assert.True(XboxLaunchRunner.IsLaunchTitleModule(Mod("isle.exe", XbdmDebugConstants.DmnModflagXbe), "default"));
    }

    [Fact]
    public void XbeAndTlsFlags_IsTheTitle()
    {
        var flags = XbdmDebugConstants.DmnModflagXbe | XbdmDebugConstants.DmnModflagTls;
        Assert.True(XboxLaunchRunner.IsLaunchTitleModule(Mod("isle.exe", flags), "default"));
    }

    [Fact]
    public void UnflaggedModuleNamedAfterTheTitle_IsTheTitle()
    {
        Assert.True(XboxLaunchRunner.IsLaunchTitleModule(Mod("triangle.exe"), "triangle"));
    }

    [Theory]
    [InlineData("xbdm.dll")]
    [InlineData("xboxkrnl.exe")]
    [InlineData("isle.exe")]
    public void UnflaggedOtherModule_IsNotTheTitle(string name)
    {
        Assert.False(XboxLaunchRunner.IsLaunchTitleModule(Mod(name), "default"));
    }

    [Fact]
    public void TlsOnlyFlag_DoesNotMakeAModuleTheTitle()
    {
        Assert.False(XboxLaunchRunner.IsLaunchTitleModule(Mod("other.dll", XbdmDebugConstants.DmnModflagTls), "default"));
    }
}
