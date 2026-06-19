using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Runs periodic usage fetches and publishes snapshots to subscribers.
/// Per spec §4.3: configurable interval, force-refresh, stale handling with last-good retention.
/// </summary>
public class Poller
{
    private readonly IUsageClient _usageClient;
    private readonly IClock _clock;
    private readonly int _pollIntervalSeconds;
    private Task? _runningTask;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The latest snapshot (null until first poll).
    /// </summary>
    public UsageSnapshot? Current { get; private set; }

    /// <summary>
    /// Fired each time a snapshot is published.
    /// </summary>
    public event Action<UsageSnapshot>? SnapshotPublished;

    /// <summary>
    /// Creates a Poller with the default 180s interval.
    /// </summary>
    public Poller(IUsageClient usageClient)
        : this(usageClient, new SystemClock(), 180) { }

    /// <summary>
    /// Creates a Poller with a custom interval and clock.
    /// </summary>
    public Poller(IUsageClient usageClient, IClock clock, int pollIntervalSeconds)
    {
        _usageClient = usageClient;
        _clock = clock;
        _pollIntervalSeconds = pollIntervalSeconds;
    }

    /// <summary>
    /// Starts the polling loop. Safe to call multiple times; subsequent calls are ignored.
    /// </summary>
    public Task StartAsync(CancellationToken ct)
    {
        if (_runningTask != null)
            return Task.CompletedTask; // Already running

        _cts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
        _runningTask = RunLoop(linkedCts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the polling loop.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _runningTask = null;
    }

    /// <summary>
    /// Triggers an immediate poll and publishes the result.
    /// </summary>
    public async Task ForceRefreshAsync()
    {
        await PublishAsync(_usageClient.FetchAsync(CancellationToken.None)).ConfigureAwait(false);
    }

    private async Task RunLoop(CancellationToken ct)
    {
        await FetchAndPublish(ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            await FetchAndPublish(ct).ConfigureAwait(false);
        }
    }

    private async Task FetchAndPublish(CancellationToken ct)
    {
        var snapshot = await _usageClient.FetchAsync(ct).ConfigureAwait(false);
        await PublishAsync(Task.FromResult(snapshot)).ConfigureAwait(false);
    }

    private async Task PublishAsync(Task<UsageSnapshot> fetchTask)
    {
        var snapshot = await fetchTask.ConfigureAwait(false);

        // Merge with last-good values
        if (snapshot.Stale && Current != null)
        {
            snapshot = MergeWithLastGood(snapshot, Current);
        }

        Current = snapshot;
        SnapshotPublished?.Invoke(snapshot);
    }

    private static UsageSnapshot MergeWithLastGood(UsageSnapshot stale, UsageSnapshot lastGood)
    {
        // Retain last-good per-limit values for stale snapshots
        var mergedLimits = new Dictionary<LimitKind, LimitState>(stale.Limits.Count);

        foreach (var kind in stale.Limits.Keys)
        {
            var staleState = stale.Limits[kind];
            // If stale limit is not present or has no data, use last-good
            if (!staleState.Present && lastGood.Limits.TryGetValue(kind, out var lastGoodState))
            {
                mergedLimits[kind] = lastGoodState;
            }
            else if (staleState.Present && staleState.Pct is null && lastGood.Limits.TryGetValue(kind, out lastGoodState))
            {
                // Stale limit is present but Pct is null — use last-good
                mergedLimits[kind] = lastGoodState;
            }
            else
            {
                mergedLimits[kind] = staleState;
            }
        }

        return new UsageSnapshot(
            mergedLimits,
            stale.UpdatedAt,
            stale.Error,
            Stale: true);
    }
}
