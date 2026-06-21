using System.Text;

namespace Rxdk.XbWatson.Core;

public sealed class WatsonLogBuffer
{
    private readonly StringBuilder _builder = new();
    private int _lineCount = 0;
    private bool _limitEnabled;

    public bool LimitBufferLength
    {
        get => _limitEnabled;
        set => _limitEnabled = value;
    }

    public string Text => _builder.ToString();

    public void Clear()
    {
        _builder.Clear();
        _lineCount = 0;
    }

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        text = NormalizeNewlines(text);

        if (_limitEnabled)
        {
            var newLines = CountNewlines(text);
            if (_lineCount + newLines > WatsonLogLimits.MaxLines)
                TrimLeadingLines(WatsonLogLimits.LinesToCut);
        }

        _builder.Append(text);
        _lineCount += CountNewlines(text);
    }

    /// <summary>Native xbWatson: newline only when the Xbox string ends with one; otherwise fragments concatenate.</summary>
    public static string FormatDebugStringForLog(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = NormalizeNewlines(text);
        if (text.EndsWith('\n'))
            return text.TrimEnd('\n') + "\n";
        return text;
    }

    internal static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private void TrimLeadingLines(int linesToRemove)
    {
        var text = _builder.ToString();
        var index = 0;
        var removed = 0;
        while (index < text.Length && removed < linesToRemove)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0)
            {
                index = text.Length;
                break;
            }

            index = next + 1;
            removed++;
        }

        _builder.Clear();
        if (index < text.Length)
            _builder.Append(text.AsSpan(index));
        _lineCount = CountNewlines(_builder.ToString());
    }

    private static int CountNewlines(string text)
    {
        var count = 0;
        foreach (var ch in text)
            if (ch == '\n')
                count++;
        return count;
    }
}
