namespace Rxdk.Xbdm.Tests.Parity;

internal static class ParityProgress
{
    internal static void Phase(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.Error.WriteLine(line);
        Console.Error.Flush();
    }

    /// <summary>Streams a single check result live, tagged with the backend it exercised.</summary>
    internal static void Result(ParityCheckResult result)
    {
        var notes = string.IsNullOrEmpty(result.Notes) ? string.Empty : $" — {result.Notes}";
        Phase($"  {result.DisplayStatus} {result.Category}/{result.Name} [{result.Backend}]{notes}");
    }
}
