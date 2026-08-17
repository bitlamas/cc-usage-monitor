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
    private readonly ILog _log;
    private readonly Func<int, CancellationToken, Task> _delay;
    private int _consecutiveFailures;
    private readonly object _lock = new();
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
    public Poller(IUsageClient usageClient, IClock clock, int pollIntervalSeconds, ILog? log = null, Func<int, CancellationToken, Task>? delay = null)
    {
        _usageClient = usageClient;
        _clock = clock;
        _pollIntervalSeconds = pollIntervalSeconds;
        _log = log ?? new NullLog();
        _delay = delay ?? ((s, ct) => Task.Delay(TimeSpan.FromSeconds(s), ct));
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
        try
        {
            await FetchAndPublish(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Exception(LogLevel.Error, "Poll", ex);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var secs = PollScheduler.NextDelaySeconds(
                    Current?.ErrorKind, Current?.RetryAfterSeconds, _pollIntervalSeconds,
                    Current?.ConsecutiveFailures ?? 0);
                await _delay(secs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await FetchAndPublish(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Exception(LogLevel.Error, "Poll", ex);
            }
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

        // Merge with last-good values (MergeWithLastGood rebuilds via ctor, so ConsecutiveFailures defaults to 0)
        if (snapshot.Stale && Current != null)
        {
            snapshot = MergeWithLastGood(snapshot, Current);
        }

        // Update counter and stamp under lock (thread-safe with ForceRefreshAsync overlap)
        UsageSnapshot stamped;
        lock (_lock)
        {
            _consecutiveFailures = snapshot.ErrorKind is null ? 0 : _consecutiveFailures + 1;
            stamped = snapshot with { ConsecutiveFailures = _consecutiveFailures };
            Current = stamped;
        }

        SnapshotPublished?.Invoke(stamped);

        // Diagnostic poll log line (on the stamped snapshot, outside the lock)
        _log.Write(stamped.ErrorKind is null ? LogLevel.Info : LogLevel.Warn, LogFormat.Poll(stamped));
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

        // Honest "updated" time: report when the data was actually fresh
        return new UsageSnapshot(
            mergedLimits,
            lastGood.UpdatedAt,
            stale.Error,
            Stale: true,
            ErrorKind: stale.ErrorKind,
            RetryAfterSeconds: stale.RetryAfterSeconds);
    }
}
