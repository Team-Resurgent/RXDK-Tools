using System.Text;

namespace Rxdk.XboxDbgBridge.Symbols;

internal sealed class VariableJson
{
    private readonly StringBuilder _builder = new();
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
    private readonly int _maxVars;
    private int _count;

    internal VariableJson(int maxVars = 256) => _maxVars = maxVars;

    internal int Count => _count;

    internal bool IsFull => _count >= _maxVars;

    internal bool WasEmitted(string name) => _emitted.Contains(name);

    internal void Append(string name, string value, bool expandable = false, string? expandBase = null)
    {
        if (IsFull || string.IsNullOrEmpty(name) || !_emitted.Add(name))
            return;

        if (_count > 0)
            _builder.Append(',');
        _builder.Append("{\"name\":");
        BridgeJsonWriter.AppendEscaped(_builder, name);
        _builder.Append(",\"value\":");
        BridgeJsonWriter.AppendEscaped(_builder, value);
        if (expandable)
        {
            _builder.Append(",\"expandable\":true,\"base\":");
            BridgeJsonWriter.AppendEscaped(_builder, expandBase ?? name);
        }

        _builder.Append('}');
        _count++;
    }

    internal string ToJsonArray() => _builder.ToString();
}
