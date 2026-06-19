namespace CcUsageMonitor.Core.Models;

/// <summary>
/// Frozen mapping: LimitKind ↔ API bucket key ↔ display label.
/// Per spec §5 — these exact strings are non-negotiable.
/// </summary>
public static class LimitKindMapping
{
    private static readonly Dictionary<LimitKind, (string BucketKey, string Label)> Mapping = new()
    {
        { LimitKind.Session5h, ("five_hour", "5-hour session") },
        { LimitKind.WeeklyAll, ("seven_day", "Weekly (all models)") },
        { LimitKind.WeeklySonnet, ("seven_day_sonnet", "Weekly (Sonnet)") },
        { LimitKind.WeeklyOpus, ("seven_day_opus", "Weekly (Opus)") }
    };

    private static readonly Dictionary<string, LimitKind> BucketKeyToKind = Mapping.ToDictionary(
        kv => kv.Value.BucketKey,
        kv => kv.Key
    );

    private static readonly Dictionary<string, LimitKind> LabelToKind = Mapping.ToDictionary(
        kv => kv.Value.Label,
        kv => kv.Key
    );

    /// <summary>All registered LimitKinds in canonical order.</summary>
    public static IReadOnlyList<LimitKind> AllKinds => Mapping.Keys.ToList();

    /// <summary>Get the API bucket key for a LimitKind.</summary>
    public static string GetBucketKey(LimitKind kind) => Mapping[kind].BucketKey;

    /// <summary>Get the display label for a LimitKind.</summary>
    public static string GetLabel(LimitKind kind) => Mapping[kind].Label;

    /// <summary>Resolve a LimitKind from its API bucket key. Returns null for unknown keys.</summary>
    public static LimitKind? GetKindFromBucketKey(string bucketKey) =>
        BucketKeyToKind.TryGetValue(bucketKey, out var kind) ? kind : null;

    /// <summary>Resolve a LimitKind from its display label. Returns null for unknown labels.</summary>
    public static LimitKind? GetKindFromLabel(string label) =>
        LabelToKind.TryGetValue(label, out var kind) ? kind : null;
}
