using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;
using Xunit;

namespace Rxdk.Xbdm.Tests;

public sealed class XbdmNotificationHubTests
{
    [Fact]
    public void FormatNotifyAtCommand_WithoutDebugSession_OmitsDebugSuffix()
    {
        var command = XbdmNotificationHub.FormatNotifyAtCommand(12345, XbdmDebugConstants.DmPersistent);
        Assert.Equal("NOTIFYAT PORT=12345", command);
    }

    [Fact]
    public void FormatNotifyAtCommand_WithDebugSession_IncludesDebugSuffix()
    {
        var flags = XbdmDebugConstants.DmDebugSession | XbdmDebugConstants.DmAsyncSession;
        var command = XbdmNotificationHub.FormatNotifyAtCommand(12345, flags);
        Assert.Equal("NOTIFYAT PORT=12345 debug", command);
    }
}
