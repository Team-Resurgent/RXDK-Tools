// SPDX-License-Identifier: GPL-3.0-or-later
// Part of RXDK-Tools - see LICENSE for the full GNU GPL v3.
//
// Diagnostic: dump the DWARF a debug .exe carries - PE sections, functions, a
// slice of the line table, and one function's locals. `dotnet run -- <file.exe>`.

using Rxdk.Dwarf;

internal static class DumpTool
{
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("usage: Rxdk.Dwarf <image.exe> [addr-hex]"); return 2; }
        var pe = PeImage.Load(args[0]);
        Console.WriteLine($"PE machine=0x{pe.Machine:X4} entry=0x{pe.Entry:X8} bigEndian={pe.IsBigEndian}");
        var dbg = pe.SectionNames.Where(s => s.StartsWith(".debug")).OrderBy(s => s).ToList();
        Console.WriteLine($".debug sections: {string.Join(", ", dbg)}");
        if (dbg.Count == 0) { Console.Error.WriteLine("no DWARF sections found"); return 1; }

        var info = DwarfReader.Read(pe);
        var funcs = info.Functions.ToList();
        int lines = info.Units.Sum(u => u.Lines.Count);
        Console.WriteLine($"units={info.Units.Count} functions={funcs.Count} lineRows={lines} types={info.Types.Count}");

        Console.WriteLine("\n-- first 12 functions --");
        foreach (var f in funcs.OrderBy(f => f.LowPc).Take(12))
            Console.WriteLine($"  {f}  vars={f.Variables.Count}");

        var firstLines = info.Units.SelectMany(u => u.Lines).Where(l => !l.EndSequence)
            .OrderBy(l => l.Address).Take(8).ToList();
        Console.WriteLine("\n-- first 8 line rows --");
        foreach (var l in firstLines) Console.WriteLine($"  {l}");

        var withVars = funcs.FirstOrDefault(f => f.Variables.Count > 0);
        if (withVars != null)
        {
            Console.WriteLine($"\n-- locals of {withVars.Name} (frameBase={BitConverter.ToString(withVars.FrameBase)}) --");
            foreach (var v in withVars.Variables) Console.WriteLine($"  {v}");
        }

        if (args.Length >= 2 && ulong.TryParse(args[1], System.Globalization.NumberStyles.HexNumber, null, out var addr))
        {
            Console.WriteLine($"\n-- lookup 0x{addr:X8} --");
            Console.WriteLine($"  function: {info.FunctionAt(addr)?.Name ?? "(none)"}");
            Console.WriteLine($"  line:     {info.LineAt(addr)}");
        }
        return 0;
    }
}
