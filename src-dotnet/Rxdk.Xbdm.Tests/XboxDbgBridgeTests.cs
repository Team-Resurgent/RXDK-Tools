using System.Diagnostics;
using Xunit;

namespace Rxdk.Xbdm.Tests;

public sealed class XboxDbgBridgeTests
{
    [Fact]
    public async Task Managed_bridge_ping_returns_pong()
    {
        var project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Rxdk.XboxDbgBridge",
            "Rxdk.XboxDbgBridge.csproj"));

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{project}\" --no-build",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var build = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{project}\" -c Debug",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(build);
        await build.WaitForExitAsync();
        Assert.Equal(0, build.ExitCode);

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        await process.StandardInput.WriteLineAsync("""{"id":1,"cmd":"ping"}""");
        await process.StandardInput.WriteLineAsync("""{"id":2,"cmd":"shutdown","rebootDashboard":false}""");
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        var lines = new List<string>();
        while (await process.StandardOutput.ReadLineAsync() is { } outputLine)
            lines.Add(outputLine);
        await process.WaitForExitAsync();

        Assert.Contains("""{"type":"event","event":"ready"}""", lines);
        Assert.Contains("""{"type":"result","id":1,"success":true,"pong":true}""", lines);
    }
}
