namespace Rxdk.Xbdm.Tests.Hardware;

internal static class KitTestProgress
{
    internal static void Phase(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.Error.WriteLine(line);
        Console.Error.Flush();
    }

    /// <summary>Streams a single check result live, tagged with the backend it exercised.</summary>
    internal static void Result(KitCheckResult result)
    {
        var notes = string.IsNullOrEmpty(result.Notes) ? string.Empty : $" — {result.Notes}";
        Phase($"  {result.DisplayStatus} {result.Category}/{result.Name} [{result.Backend}]{notes}");
    }
}
