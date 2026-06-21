using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;
using Xunit;

namespace Rxdk.Xbdm.Tests;

public sealed class XbdmNotificationParserTests
{
    [Fact]
    public void Parses_break_with_stop_flag()
    {
        Assert.True(XbdmNotificationParser.TryHandleNotification(
            "break addr=0x100 thread=2 stop",
            out var dispatches));
        Assert.Single(dispatches);
        Assert.Equal(XbdmDebugConstants.DmBreak | XbdmDebugConstants.StopThread, dispatches[0].Code);
        var brk = Assert.IsType<XbdmBreakNotification>(dispatches[0].Data);
        Assert.Equal((nuint)0x100, brk.Address);
        Assert.Equal(2u, brk.ThreadId);
    }

    [Fact]
    public void Dispatches_first_execution_notification_even_when_state_matches_default()
    {
        XbdmNotificationParser.ResetExecState();
        Assert.True(XbdmNotificationParser.TryHandleNotification("execution started", out var first));
        Assert.Equal(XbdmDebugConstants.DmnExecStart, first[0].Data);
        Assert.False(XbdmNotificationParser.TryHandleNotification("execution started", out _));
    }

    [Fact]
    public void Parses_execution_state_change_once()
    {
        XbdmNotificationParser.ResetExecState();
        Assert.True(XbdmNotificationParser.TryHandleNotification("execution stopped", out var first));
        Assert.Equal((uint)XbdmDebugConstants.DmExec, first[0].Code & XbdmDebugConstants.NotificationMask);
        Assert.Equal(XbdmDebugConstants.DmnExecStop, first[0].Data);

        Assert.False(XbdmNotificationParser.TryHandleNotification("execution stopped", out _));
        Assert.True(XbdmNotificationParser.TryHandleNotification("execution started", out var second));
        Assert.Equal(XbdmDebugConstants.DmnExecStart, second[0].Data);
    }

    [Fact]
    public void Coalesces_assert_until_prompt()
    {
        XbdmAssertBuffer.Reset();
        Assert.False(XbdmNotificationParser.TryHandleNotification(
            "assert thread=4 string=\"part1\"",
            out _));
        Assert.True(XbdmNotificationParser.TryHandleNotification(
            "assert thread=4 string=\"part2\" prompt",
            out var dispatches));
        var assert = Assert.IsType<XbdmDebugStringNotification>(dispatches[0].Data);
        Assert.Equal(4u, assert.ThreadId);
        Assert.Equal("part1 part2", assert.Text);
    }

    [Fact]
    public void Parses_section_and_fiber_notifications()
    {
        Assert.True(XbdmNotificationParser.TryHandleNotification(
            "sectload name=\".text\" base=0x400000 size=0x1000 index=1 flags=0",
            out var sectionDispatch));
        var section = Assert.IsType<XbdmSectionLoadNotification>(sectionDispatch[0].Data);
        Assert.Equal(".text", section.Name);

        Assert.True(XbdmNotificationParser.TryHandleNotification(
            "fiber id=7 start=0x80001000",
            out var fiberDispatch));
        var fiber = Assert.IsType<XbdmFiberNotification>(fiberDispatch[0].Data);
        Assert.True(fiber.Create);
        Assert.Equal(7u, fiber.FiberId);
    }

    [Fact]
    public void Identifies_external_notification_prefix()
    {
        Assert.True(XbdmNotificationParser.TryGetExternalPrefix("foo!notify data=1", out var prefix));
        Assert.Equal("foo", prefix);
    }

    [Fact]
    public void Parses_debugstr_with_lf_suffix()
    {
        Assert.True(XbdmNotificationParser.TryHandleNotification(
            "debugstr thread=0 string=Connected lf",
            out var dispatches));
        var debugStr = Assert.IsType<XbdmDebugStringNotification>(dispatches[0].Data);
        Assert.Equal("Connected\n", debugStr.Text);
    }

    [Fact]
    public void Parses_debugstr_with_spaced_value_and_lf()
    {
        Assert.True(XbdmNotificationParser.TryHandleNotification(
            "debugstr thread=0 string= client: 1 lf",
            out var dispatches));
        var debugStr = Assert.IsType<XbdmDebugStringNotification>(dispatches[0].Data);
        Assert.Equal("client: 1\n", debugStr.Text);
    }

    [Fact]
    public void Parses_debugstr_http_trace_line()
    {
        Assert.True(XbdmNotificationParser.TryHandleNotification(
            "debugstr thread=0 string=Header: Host: 192.168.1.188 lf",
            out var dispatches));
        var debugStr = Assert.IsType<XbdmDebugStringNotification>(dispatches[0].Data);
        Assert.Equal("Header: Host: 192.168.1.188\n", debugStr.Text);
    }

    [Fact]
    public void Parses_debugstr_quoted_value_with_spaces()
    {
        Assert.True(XbdmNotificationParser.TryHandleNotification(
            "debugstr thread=0 string=\"Request: GET /api/status.json HTTP/1.1\" lf",
            out var dispatches));
        var debugStr = Assert.IsType<XbdmDebugStringNotification>(dispatches[0].Data);
        Assert.Equal("Request: GET /api/status.json HTTP/1.1\n", debugStr.Text);
    }
}
