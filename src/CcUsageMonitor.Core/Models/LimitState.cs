namespace CcUsageMonitor.Core.Models;

/// <summary>
/// Per-limit usage state. Per spec §5:
///   Pct: null when API returns null/missing/non-numeric utilization; unclamped (>100 allowed).
///   ResetsAt: null if absent; otherwise parsed reset instant (carries offset).
///   Present: false if the API did not return this bucket.
/// </summary>
public record LimitState(int? Pct, DateTimeOffset? ResetsAt, bool Present);
