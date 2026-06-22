using System.Globalization;

namespace Rxdk.XbWatson.Core;

public static class WatsonLogFormatter
{
    /// <summary>
    /// Prefixes log text with a local wall-clock stamp matching native xbWatson ctime output: [%s] message.
    /// </summary>
    public static string WithTimestamp(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var stamp = DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);
        return $"[{stamp}] {text}";
    }
}
