using Rxdk.XbWatson.Core;
using Rxdk.XbWatson.Core.BreakInfo;

namespace Rxdk.XbWatson.Core.Tests;

public class WatsonLogFormatterTests
{
    [Fact]
    public void WithTimestamp_PrefixesBracketedLocalTime()
    {
        var result = WatsonLogFormatter.WithTimestamp("hello\r\n");
        Assert.StartsWith("[", result);
        Assert.EndsWith("] hello\r\n", result);
    }
}

public class WatsonKernelDebugTests
{
    [Fact]
    public void IsKernelDebugString_ThreadZeroOnly()
    {
        Assert.True(WatsonKernelDebug.IsKernelDebugString(0));
        Assert.False(WatsonKernelDebug.IsKernelDebugString(1));
    }

    [Fact]
    public void ResponseMentionsDpctrace_DetectsKeyword()
    {
        Assert.True(WatsonKernelDebug.ResponseMentionsDpctrace("dpctrace=1"));
        Assert.False(WatsonKernelDebug.ResponseMentionsDpctrace("other=1"));
    }
}

public class WatsonLogBufferTests
{
    [Fact]
    public void FormatDebugStringForLog_MatchesNativeNewlineRules()
    {
        Assert.Equal("hello\n", WatsonLogBuffer.FormatDebugStringForLog("hello\n"));
        Assert.Equal("hello\n", WatsonLogBuffer.FormatDebugStringForLog("hello\r\n"));
        Assert.Equal("hello", WatsonLogBuffer.FormatDebugStringForLog("hello"));
        Assert.Equal("Header: ", WatsonLogBuffer.FormatDebugStringForLog("Header: "));
    }

    [Fact]
    public void LimitBuffer_TrimsWhenExceedingMaxLines()
    {
        var buffer = new WatsonLogBuffer { LimitBufferLength = true };
        var line = new string('a', 10) + "\n";
        for (var i = 0; i < WatsonLogLimits.MaxLines + 10; i++)
            buffer.Append(line);

        var lines = buffer.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= WatsonLogLimits.MaxLines);
    }
}

public class WatsonAssertParserTests
{
    [Fact]
    public void Parse_StandardAssert_ExtractsFields()
    {
        var text = "File: foo.cpp\nLine: 42\nExpression: x > 0\n";
        var display = WatsonAssertParser.Parse(text, "d:\\game\\title.xbe");
        Assert.False(display.IsAbortStyle);
        Assert.Equal("foo.cpp", display.File);
        Assert.Equal("42", display.Line);
        Assert.Equal("x > 0", display.Expression);
    }

    [Fact]
    public void Parse_AbortStyle_WhenNoFilePrefix()
    {
        var display = WatsonAssertParser.Parse("abort message", "d:\\game\\title.xbe");
        Assert.True(display.IsAbortStyle);
        Assert.Contains("Runtime", display.AbortLine1);
    }
}

public class BreakInfoBinaryTests
{
    [Fact]
    public void WriterReader_RoundTrip()
    {
        var original = new WatsonBreakInfo
        {
            EventType = WatsonDialogIds.Exception,
            BrokenThreadId = 7,
            EventCode = WatsonExceptionCodes.AccessViolation,
            WriteException = true,
            AvAddress = 0x12345678,
            XboxName = "MYXBOX",
            SystemTime = new DateTime(2024, 6, 1, 12, 30, 0),
            AppName = "d:\\default.xbe",
            FirstSectionBase = 0x400000,
            Modules = new List<NativeModLoad>
            {
                new()
                {
                    Name = "game.exe",
                    BaseAddress = 0x400000,
                    Size = 0x100000,
                    TimeStamp = 1,
                    CheckSum = 2,
                    Flags = 1,
                },
            },
            Threads = new List<WatsonThreadBreakInfo>
            {
                new()
                {
                    IsValid = true,
                    ThreadId = 7,
                    StackBase = 0x70000,
                    StackSize = 4,
                    StackBytes = new byte[] { 1, 2, 3, 4 },
                    Context = new Rxdk.Xbdm.XbdmContext { Eip = 0x401000, Esp = 0x70000 },
                },
            },
        };

        using var stream = new MemoryStream();
        BreakInfoWriter.Write(stream, original);
        stream.Position = 0;
        var read = BreakInfoReader.Read(stream);
        Assert.NotNull(read);
        Assert.Equal(original.EventType, read!.EventType);
        Assert.Equal(original.BrokenThreadId, read.BrokenThreadId);
        Assert.Equal(original.EventCode, read.EventCode);
        Assert.Equal(original.XboxName, read.XboxName);
        Assert.Equal(original.AppName, read.AppName);
        Assert.Single(read.Modules);
        Assert.Equal("game.exe", read.Modules[0].Name);
        Assert.Single(read.Threads);
        Assert.Equal(7u, read.Threads[0].ThreadId);
    }
}
