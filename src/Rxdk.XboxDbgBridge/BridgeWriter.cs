using System.Text;

namespace Rxdk.XboxDbgBridge;

internal static class BridgeWriter
{
    private static readonly object Gate = new();

    internal static void Emit(string json)
    {
        lock (Gate)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    internal static void Log(string message) =>
        Console.Error.WriteLine(message);

    internal static void EmitResult(int id, bool success, string? extraFields = null)
    {
        var builder = new StringBuilder();
        builder.Append("{\"type\":\"result\",\"id\":").Append(id)
            .Append(",\"success\":").Append(success ? "true" : "false");
        if (!string.IsNullOrEmpty(extraFields))
        {
            builder.Append(',');
            builder.Append(extraFields);
        }

        builder.Append('}');
        Emit(builder.ToString());
    }

    internal static void EmitEvent(string name, string? fields = null)
    {
        if (string.IsNullOrEmpty(fields))
            Emit($"{{\"type\":\"event\",\"event\":\"{name}\"}}");
        else
            Emit($"{{\"type\":\"event\",\"event\":\"{name}\",{fields}}}");
    }
}
