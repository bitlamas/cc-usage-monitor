using System;
using System.Collections.Generic;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Monitors usage snapshots and fires one toast per limit when pct >= alertThreshold.
/// Per spec §4.6: re-arm only on strictly greater ResetsAt, null-reset handling,
/// unavailable sink still records as fired.
/// </summary>
public class AlertManager
{
    private readonly INotificationSink _sink;
    private readonly IClock _clock;
    private readonly int _alertThreshold;
    private readonly int _warnThreshold;

    /// <summary>
    /// Records of which limits have been alerted and at what ResetsAt.
    /// Key = LimitKind, Value = ResetsAt observed at alert time.
    /// </summary>
    private readonly Dictionary<LimitKind, DateTimeOffset?> _alerted = new();

    /// <summary>
    /// Whether alerts are currently enabled.
    /// </summary>
    public bool AlertsEnabled { get; set; } = true;

    /// <summary>
    /// Creates an AlertManager with default thresholds.
    /// </summary>
    public AlertManager(INotificationSink sink)
        : this(sink, new SystemClock(), 90, 70) { }

    /// <summary>
    /// Creates an AlertManager with custom thresholds.
    /// </summary>
    public AlertManager(INotificationSink sink, IClock clock, int alertThreshold, int warnThreshold)
    {
        _sink = sink;
        _clock = clock;
        _alertThreshold = alertThreshold;
        _warnThreshold = warnThreshold;
    }

    /// <summary>
    /// Processes a snapshot, firing toasts as needed.
    /// Per spec §4.6 re-arm predicate:
    ///   observed.ResetsAt is not null && (recorded.ResetsAt is null || observed > recorded)
    /// </summary>
    public void OnSnapshot(UsageSnapshot snapshot)
    {
        if (!AlertsEnabled)
            return;

        foreach (var (kind, state) in snapshot.Limits)
        {
            // Only fire when pct >= alertThreshold
            if (state.Pct is not int pct || pct < _alertThreshold)
                continue;

            // Check re-arm condition
            if (!ShouldFire(kind, state.ResetsAt))
                continue;

            // Fire the alert
            FireAlert(kind, pct, state.ResetsAt);

            // Record the alert
            _alerted[kind] = state.ResetsAt;
        }
    }

    /// <summary>
    /// Re-arm predicate per spec §4.6:
    ///   observed.ResetsAt is not null && (recorded.ResetsAt is null || observed > recorded)
    /// </summary>
    private bool ShouldFire(LimitKind kind, DateTimeOffset? observedResetsAt)
    {
        if (!_alerted.TryGetValue(kind, out var recordedResetsAt))
            return true; // Never alerted before → fire

        // Re-arm only if observed.ResetsAt is not null AND (recorded is null OR observed > recorded)
        if (observedResetsAt is null)
            return false; // Crossing with null reset → record but don't re-arm

        return recordedResetsAt is null || observedResetsAt.Value > recordedResetsAt.Value;
    }

    private void FireAlert(LimitKind kind, int pct, DateTimeOffset? resetsAt)
    {
        var title = UsageText.ToastTitle(pct);
        var body = UsageText.ToastBody(kind, resetsAt, _clock.Now);

        // Try to show the toast. Even if unavailable, the alert is recorded as fired.
        _ = _sink.TryShow(title, body);
    }
}
