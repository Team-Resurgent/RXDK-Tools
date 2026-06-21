namespace Rxdk.Xbdm.Tests.Hardware;

public enum KitCheckStatus
{
    Passed,
    Failed,
    Skipped,
}

public sealed record KitCheckResult(
    string Category,
    string Name,
    KitCheckStatus Status,
    string? NativeDetail = null,
    string? ManagedDetail = null,
    string? Notes = null,
    bool ExpectedFailure = false,
    string? BackendOverride = null)
{
    public string DisplayStatus => Status switch
    {
        KitCheckStatus.Passed when ExpectedFailure => "PASS (expected fail)",
        KitCheckStatus.Passed => "PASS",
        KitCheckStatus.Failed => "FAIL",
        KitCheckStatus.Skipped => "SKIP",
        _ => Status.ToString(),
    };

    public string Backend => BackendOverride ?? TestBackend.Describe(Category);

    public override string ToString() =>
        $"[{DisplayStatus}] {Category}/{Name} [{Backend}]" +
        (Notes is not null ? $": {Notes}" : string.Empty) +
        (ManagedDetail is not null ? $" ({ManagedDetail})" : string.Empty);
}

public static class TestBackend
{
    public const string Managed = "managed";
    public const string ManagedBridge = "managed xboxdbg-bridge";
    public const string Harness = "test harness";

    public static string Describe(string category) => category switch
    {
        "Bridge" => ManagedBridge,
        _ => Managed,
    };
}

public sealed class KitTestReportSummary
{
    public required IReadOnlyList<KitCheckResult> Results { get; init; }
    public required string ConsoleName { get; init; }
    public required bool PasswordConfigured { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public int Passed => Results.Count(r => r.Status == KitCheckStatus.Passed);
    public int ExpectedPasses => Results.Count(r => r.Status == KitCheckStatus.Passed && r.ExpectedFailure);
    public int Failed => Results.Count(r => r.Status == KitCheckStatus.Failed);
    public int Skipped => Results.Count(r => r.Status == KitCheckStatus.Skipped);
    public int Total => Results.Count;

    public string FormatSummaryLine()
    {
        var passed = ExpectedPasses > 0
            ? $"{Passed} passed ({ExpectedPasses} expected fail)"
            : $"{Passed} passed";
        return $"{passed}, {Failed} failed, {Skipped} skipped ({Total} total)";
    }
}
