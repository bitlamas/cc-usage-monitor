namespace CcUsageMonitor.Core.Models;

/// <summary>
/// Full usage snapshot for one poll cycle. Per spec §5.
///   Limits: per-limit state.
///   UpdatedAt: when the API returned this data.
///   Error: null on success; carries a short technical reason on failure.
///   Stale: true when the latest fetch failed.
/// </summary>
public record UsageSnapshot(
    IReadOnlyDictionary<LimitKind, LimitState> Limits,
    DateTimeOffset UpdatedAt,
    string? Error,
    bool Stale,
    Core.Services.ErrorKind? ErrorKind = null,
    int? RetryAfterSeconds = null);
