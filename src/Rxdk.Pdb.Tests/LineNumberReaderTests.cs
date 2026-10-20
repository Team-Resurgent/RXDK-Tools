using Rxdk.Pdb;

namespace Rxdk.Pdb.Tests;

/// <summary>
/// Exercises the C13 line-number reader (address ↔ file:line) and symbol-by-name lookups that back
/// managed, dbghelp-free source debugging. MiniLocals.pdb is clang -gcodeview (the same C13 line
/// format Zig emits for RXDK titles).
/// </summary>
public class LineNumberReaderTests
{
    private static string MiniPdb => Path.Combine(AppContext.BaseDirectory, "TestData", "MiniLocals.pdb");

    [Fact]
    public void MiniLocals_ParsesLineEntries()
    {
        var pdb = PdbImage.OpenFile(MiniPdb);
        var entries = pdb.Lines.Entries;

        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.True(e.Line > 0, "line number should be positive");
            Assert.True(e.RvaEnd > e.RvaStart, "each run must cover at least one byte");
        });
        Assert.Contains(entries, e =>
            e.File.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ||
            e.File.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) ||
            e.File.EndsWith(".cc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MiniLocals_RoundTripsRvaAndLine()
    {
        var pdb = PdbImage.OpenFile(MiniPdb);
        var entry = pdb.Lines.Entries.First();

        Assert.True(pdb.TryFindLine(entry.RvaStart, out var file, out var line));
        Assert.Equal(entry.Line, line);
        Assert.Equal(entry.File, file);

        Assert.True(pdb.TryResolveLine(file, line, out var rva));
        Assert.True(pdb.TryFindLine(rva, out _, out var backLine));
        Assert.Equal(line, backLine);
    }

    [Fact]
    public void MiniLocals_ResolvesFunctionSymbolBothWays()
    {
        var pdb = PdbImage.OpenFile(MiniPdb);
        var fn = pdb.Symbols.EnumerateFunctions().FirstOrDefault(f => f.CodeSize > 0);

        Assert.NotNull(fn);
        Assert.True(pdb.TryFindSymbolRva(fn!.FunctionName, out var rva));
        Assert.Equal(fn.FunctionRva, rva);

        Assert.True(pdb.TryFindFunctionName(fn.FunctionRva, out var name));
        Assert.Equal(fn.FunctionName, name);
    }

    [Fact]
    public void TryFindLine_ReturnsFalse_ForUnmappedAddress()
    {
        var pdb = PdbImage.OpenFile(MiniPdb);
        // Far below any code section RVA -- must not resolve.
        Assert.False(pdb.TryFindLine(0, out _, out _));
    }
}
