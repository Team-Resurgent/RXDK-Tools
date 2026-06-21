using System.Globalization;
using System.Text.Json;

namespace Rxdk.XboxDbgBridge;

internal static class BridgeJson
{
    internal static bool TryGetString(JsonElement root, string key, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(key, out var property))
            return false;
        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        return false;
    }

    internal static bool TryGetUInt32(JsonElement root, string key, out uint value)
    {
        value = 0;
        if (!root.TryGetProperty(key, out var property))
            return false;

        switch (property.ValueKind)
        {
            case JsonValueKind.Number when property.TryGetUInt32(out value):
                return true;
            case JsonValueKind.String:
                var text = property.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return false;
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
                return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    internal static bool TryGetBool(JsonElement root, string key, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(key, out var property))
            return false;
        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
            return true;

        return false;
    }

    internal static bool TryGetAddress(JsonElement root, string key, out nuint value)
    {
        value = 0;
        if (!root.TryGetProperty(key, out var property))
            return false;

        if (property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return nuint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return nuint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt32(out var dword))
        {
            value = dword;
            return true;
        }

        return false;
    }
}
