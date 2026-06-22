using System.Text;
using Rxdk.XboxDbgBridge.Interop;

namespace Rxdk.XboxDbgBridge.Symbols;

internal static class MapFileGlobals
{
    internal static uint? ReadLinkBase(string mapPath)
    {
        foreach (var line in File.ReadLines(mapPath))
        {
            const string marker = " Preferred load address is ";
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                continue;
            var value = line[(index + marker.Length)..].Trim();
            if (uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var baseAddr))
                return baseAddr;
        }

        return null;
    }

    internal static void Emit(string mapPath, uint linkBase, nuint moduleBase, VariableJson variables, int maxVars)
    {
        foreach (var pass in new[] { "Publics by Value", "Static symbols" })
        {
            var inSection = false;
            foreach (var line in File.ReadLines(mapPath))
            {
                if (!inSection)
                {
                    if (line.Contains(pass, StringComparison.Ordinal))
                        inSection = true;
                    continue;
                }

                if (IsSectionBoundary(line))
                    break;
                if (variables.Count >= maxVars)
                    return;

                if (!TryParsePublicLine(line, out var sect, out var linkAddr, out var mangled, out var obj))
                    continue;
                if (sect != 3 || !IsTitleMapObject(obj))
                    continue;
                if (mangled.StartsWith("__", StringComparison.Ordinal))
                    continue;
                // Compiler-generated symbols: string literals ($SG...), scratch ($S...), and other
                // '$'-prefixed temporaries are not user globals.
                if (mangled.StartsWith("$", StringComparison.Ordinal) ||
                    mangled.StartsWith("??_C", StringComparison.Ordinal))
                    continue;

                var display = Undecorate(mangled);
                if (string.IsNullOrEmpty(display) || display.StartsWith("$", StringComparison.Ordinal))
                    continue;

                var size = GuessSize(mangled);
                nuint runtimeAddr = 0;
                if (moduleBase != 0)
                    runtimeAddr = moduleBase + (linkAddr - linkBase);

                var value = size > 4
                    ? $"{{{size} bytes}}"
                    : runtimeAddr != 0
                        ? $"@0x{runtimeAddr:x8}"
                        : "-";
                variables.Append(display, value, expandable: size > 4, expandBase: display);
            }
        }
    }

    private static bool IsSectionBoundary(string line) =>
        line.Contains("Publics by RVA", StringComparison.Ordinal) ||
        line.Contains("Static symbols", StringComparison.Ordinal) ||
        line.Contains("Line numbers", StringComparison.Ordinal) ||
        line.Contains("entry point at", StringComparison.Ordinal);

    private static bool IsTitleMapObject(string obj) =>
        !string.IsNullOrEmpty(obj) &&
        !obj.Contains(':') &&
        obj.EndsWith(".obj", StringComparison.OrdinalIgnoreCase);

    private static uint GuessSize(string mangled)
    {
        if (mangled.Contains("@@3T_LARGE_INTEGER@@", StringComparison.Ordinal))
            return 8;
        if (mangled.Contains("@@3U_D3DPRESENT_PARAMETERS@@", StringComparison.Ordinal))
            return 48;
        return 4;
    }

    private static string Undecorate(string mangled)
    {
        var builder = new StringBuilder(256);
        if (DbgHelpNative.UnDecorateSymbolName(mangled, builder, builder.Capacity, DbgHelpNative.UndnameNameOnly) == 0)
            return mangled;
        return builder.ToString();
    }

    private static bool TryParsePublicLine(
        string line,
        out uint sect,
        out uint linkAddr,
        out string mangled,
        out string obj)
    {
        sect = 0;
        linkAddr = 0;
        mangled = string.Empty;
        obj = string.Empty;
        line = line.TrimStart();
        if (line.Length == 0 || line[0] < '0' || line[0] > '9')
            return false;

        var firstSpace = line.IndexOfAny([' ', '\t']);
        var sectToken = firstSpace > 0 ? line[..firstSpace] : line;
        var colon = sectToken.IndexOf(':');
        if (colon > 0)
        {
            uint.TryParse(sectToken[..colon], System.Globalization.NumberStyles.HexNumber, null, out sect);
            uint.TryParse(sectToken[(colon + 1)..], System.Globalization.NumberStyles.HexNumber, null, out _);
        }
        else if (!uint.TryParse(sectToken, System.Globalization.NumberStyles.HexNumber, null, out sect))
        {
            return false;
        }

        var rest = firstSpace > 0 ? line[(firstSpace + 1)..].TrimStart() : string.Empty;
        var mangledEnd = rest.IndexOfAny([' ', '\t']);
        mangled = mangledEnd > 0 ? rest[..mangledEnd] : rest;
        if (string.IsNullOrEmpty(mangled) || mangled[0] == '.')
            return false;

        var tail = mangledEnd > 0 ? rest[(mangledEnd + 1)..].TrimStart() : string.Empty;
        var linkEnd = tail.IndexOfAny([' ', '\t']);
        var linkToken = linkEnd > 0 ? tail[..linkEnd] : tail;
        if (!uint.TryParse(linkToken, System.Globalization.NumberStyles.HexNumber, null, out linkAddr))
            linkAddr = 0;

        var objTail = linkEnd > 0 ? tail[(linkEnd + 1)..].Trim() : string.Empty;
        while (objTail.Length > 0 && (objTail[^1] is ' ' or '\t'))
            objTail = objTail[..^1];
        var objStart = objTail.LastIndexOfAny([' ', '\t']) + 1;
        obj = objStart > 0 ? objTail[objStart..] : objTail;
        return obj.Contains(".obj", StringComparison.OrdinalIgnoreCase);
    }
}
