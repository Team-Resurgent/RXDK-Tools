namespace Rxdk.XbWatson.Core;

public static class WatsonAssertParser
{
    private const string FilePrefix = "File: ";
    private const string LinePrefix = "Line: ";
    private const string ExpressionPrefix = "Expression: ";

    public static WatsonAssertDisplay Parse(string assertText, string launchPath)
    {
        if (assertText.Contains(FilePrefix, StringComparison.Ordinal))
        {
            var file = ExtractField(assertText, FilePrefix);
            var line = ExtractField(assertText, LinePrefix);
            var expression = ExtractField(assertText, ExpressionPrefix);
            return new WatsonAssertDisplay
            {
                IsAbortStyle = false,
                TitleProgram = launchPath,
                File = file,
                Line = line,
                Expression = expression,
            };
        }

        return new WatsonAssertDisplay
        {
            IsAbortStyle = true,
            TitleProgram = launchPath,
            AbortLine1 = "This application has requested the Runtime to terminate it",
            AbortLine2 = "in an unusual way.  Please contact the application's support",
            AbortLine3 = "team for more information.",
        };
    }

    private static string ExtractField(string text, string prefix)
    {
        var start = text.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;
        start += prefix.Length;
        var end = text.IndexOf('\n', start);
        if (end < 0)
            return text[start..].TrimEnd('\r');
        return text[start..end].TrimEnd('\r');
    }
}

public sealed class WatsonAssertDisplay
{
    public bool IsAbortStyle { get; init; }
    public string TitleProgram { get; init; } = "";
    public string File { get; init; } = "";
    public string Line { get; init; } = "";
    public string Expression { get; init; } = "";
    public string AbortLine1 { get; init; } = "";
    public string AbortLine2 { get; init; } = "";
    public string AbortLine3 { get; init; } = "";
}
