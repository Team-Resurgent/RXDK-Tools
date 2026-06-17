using System.Globalization;

namespace Rxdk.Xbdm.Managed;

/// <summary>Faithful port of <c>PchGetParam</c>, <c>FGetDwParam</c>, <c>FGetSzParam</c>, <c>FGetQwordParam</c> from protocol.c.</summary>
internal static class XbdmParamParser
{
    internal static bool TryGetParam(ReadOnlySpan<char> line, ReadOnlySpan<char> key, bool needValue, bool noCommand)
    {
        return TryGetParamValue(line, key, needValue, noCommand, out _);
    }

    internal static bool TryGetParamValue(
        ReadOnlySpan<char> line,
        ReadOnlySpan<char> key,
        bool needValue,
        bool noCommand,
        out ReadOnlySpan<char> value)
    {
        value = default;
        var index = 0;
        if (!noCommand)
        {
            while (index < line.Length && !IsSpace(line[index]))
                index++;
        }

        var inQuotes = false;
        while (index < line.Length)
        {
            while (index < line.Length && IsSpace(line[index]))
                index++;
            if (index >= line.Length)
                return false;

            var tokenStart = index;
            var equalsIndex = -1;
            while (index < line.Length && (!IsSpace(line[index]) || inQuotes))
            {
                if (line[index] == '=')
                {
                    equalsIndex = index;
                    break;
                }

                index++;
            }

            if (equalsIndex >= 0)
            {
                var tokenKey = line[tokenStart..equalsIndex];
                if (KeyEquals(tokenKey, key))
                {
                    value = line[(equalsIndex + 1)..];
                    return true;
                }

                index = equalsIndex + 1;
                while (index < line.Length && (!IsSpace(line[index]) || inQuotes))
                {
                    if (line[index] == '"')
                        inQuotes = !inQuotes;
                    index++;
                }

                continue;
            }

            var bareToken = line[tokenStart..index];
            if (!needValue && KeyEquals(bareToken, key))
            {
                value = bareToken;
                return true;
            }

            while (index < line.Length && (!IsSpace(line[index]) || inQuotes))
            {
                if (line[index] == '"')
                    inQuotes = !inQuotes;
                index++;
            }
        }

        return false;
    }

    internal static bool TryGetSzParam(ReadOnlySpan<char> line, ReadOnlySpan<char> key, Span<char> destination, out int written)
    {
        written = 0;
        destination.Clear();
        if (!TryGetParamValue(line, key, true, true, out var value))
            return false;

        var inQuotes = false;
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && IsSpace(ch))
                break;

            if (written + 1 >= destination.Length)
                return false;

            destination[written++] = ch;
        }

        return true;
    }

    internal static string? GetSzParam(string line, string key)
    {
        Span<char> buffer = stackalloc char[512];
        return TryGetSzParam(line, key, buffer, out var written)
            ? new string(buffer[..written])
            : null;
    }

    internal static bool TryGetDwParamFromSz(ReadOnlySpan<char> value, out uint result)
    {
        result = 0;
        value = TrimTokenSuffix(value);
        if (value.IsEmpty)
            return false;

        if (value[0] == '0')
        {
            if (value.Length > 1 && (value[1] == 'x' || value[1] == 'X'))
            {
                return uint.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
            }

            return false;
        }

        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    internal static bool TryGetDwParam(string line, string key, out uint value)
    {
        value = 0;
        if (!TryGetParamValue(line, key, true, true, out var raw))
            return false;
        return TryGetDwParamFromSz(raw, out value);
    }

    internal static uint GetDwParam(string line, string key, uint defaultValue = 0) =>
        TryGetDwParam(line, key, out var value) ? value : defaultValue;

    internal static bool TryGetQwordParam(string line, string key, out ulong value)
    {
        value = 0;
        Span<char> buffer = stackalloc char[32];
        if (!TryGetSzParam(line, key, buffer, out var written))
            return false;

        var sz = buffer[..written];
        if (sz.Length < 3 || !(sz[0] == '0' && (sz[1] == 'q' || sz[1] == 'Q')))
            return false;

        var hexCount = 0;
        for (var i = 2; i < sz.Length && !IsSpace(sz[i]); i++)
        {
            var ch = sz[i];
            if (!IsHexDigit(ch))
                return false;
            hexCount++;
        }

        if (hexCount <= 0)
            return false;

        var hex = sz[2..(2 + hexCount)].ToString().PadLeft(16, '0');
        if (!uint.TryParse(hex[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high) ||
            !uint.TryParse(hex[8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low))
        {
            return false;
        }

        value = ((ulong)high << 32) | low;
        return true;
    }

    internal static ReadOnlySpan<char> TrimTokenSuffix(ReadOnlySpan<char> value)
    {
        var end = value.Length;
        while (end > 0 && (value[end - 1] == ',' || value[end - 1] == ';'))
            end--;

        for (var i = 0; i < end; i++)
        {
            if (IsSpace(value[i]))
            {
                end = i;
                break;
            }
        }

        return value[..end];
    }

    private static bool KeyEquals(ReadOnlySpan<char> token, ReadOnlySpan<char> key) =>
        token.Equals(key, StringComparison.OrdinalIgnoreCase);

    private static bool IsSpace(char ch) => ch is '\0' or ' ' or '\t' or '\r' or '\n';

    private static bool IsHexDigit(char ch) =>
        ch is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
}
