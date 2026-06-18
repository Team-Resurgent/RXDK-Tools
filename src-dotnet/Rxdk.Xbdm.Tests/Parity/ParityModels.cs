namespace Rxdk.Xbdm.Tests.Parity;

public enum ParityStatus
{
    Passed,
    Failed,
    Skipped,
}

public sealed record ParityCheckResult(
    string Category,
    string Name,
    ParityStatus Status,
    string? NativeDetail = null,
    string? ManagedDetail = null,
    string? Notes = null,
    bool ExpectedFailure = false,
    string? BackendOverride = null)
{
    public string DisplayStatus => Status switch
    {
        ParityStatus.Passed when ExpectedFailure => "PASS (expected fail)",
        ParityStatus.Passed => "PASS",
        ParityStatus.Failed => "FAIL",
        ParityStatus.Skipped => "SKIP",
        _ => Status.ToString(),
    };

    /// <summary>Which implementation(s) this check actually exercises (see <see cref="ParityBackend"/>).</summary>
    public string Backend => BackendOverride ?? ParityBackend.Describe(Category);

    public override string ToString() =>
        $"[{DisplayStatus}] {Category}/{Name} [{Backend}]" +
        (Notes is not null ? $": {Notes}" : string.Empty) +
        (NativeDetail is not null || ManagedDetail is not null
            ? $" (native={NativeDetail ?? "-"}, managed={ManagedDetail ?? "-"})"
            : string.Empty);
}

/// <summary>
/// Maps a parity category to the implementations it compares. Security mutations run through
/// managed (the shipping client); native xbdm.dll is consulted read-only for parity.
/// </summary>
public static class ParityBackend
{
    public const string NativeVsManaged = "native xbdm.dll vs managed";
    public const string ManagedVsManaged = "managed proxy vs managed";
    public const string ManagedOnly = "managed only";
    public const string Mixed = "mixed: xbdm.dll + managed proxy";
    public const string ManagedBridge = "managed xboxdbg-bridge";
    public const string Harness = "test harness";

    public static string Describe(string category) => category switch
    {
        "Connection" or "File" or "Reboot" or "Security" => NativeVsManaged,
        "Debug" or "Extended" or "Execution" => ManagedVsManaged,
        "Launch" => Mixed,
        "Bridge" => ManagedBridge,
        _ => Harness,
    };
}

public sealed class ParityReportSummary
{
    public required IReadOnlyList<ParityCheckResult> Results { get; init; }
    public required string ConsoleName { get; init; }
    public required bool PasswordConfigured { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public int Passed => Results.Count(r => r.Status == ParityStatus.Passed);
    public int ExpectedPasses => Results.Count(r => r.Status == ParityStatus.Passed && r.ExpectedFailure);
    public int Failed => Results.Count(r => r.Status == ParityStatus.Failed);
    public int Skipped => Results.Count(r => r.Status == ParityStatus.Skipped);
    public int Total => Results.Count;

    public string FormatSummaryLine()
    {
        var passed = ExpectedPasses > 0
            ? $"{Passed} passed ({ExpectedPasses} expected fail)"
            : $"{Passed} passed";
        return $"{passed}, {Failed} failed, {Skipped} skipped ({Total} total)";
    }
}
