using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using CcUsageMonitor.Core.Tests.Fakes;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class PollerTests
{
    #region Basic publish

    [Fact]
    public async Task Poller_PublishesSnapshotOnForceRefresh()
    {
        // Arrange
        var publishedSnapshots = new List<UsageSnapshot>();
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(
            new UsageSnapshot(
                BuildLimitState(50, "2026-06-19T18:00:00+00:00", true),
                DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"),
                null, false)));
        var sut = new Poller(fakeClient, new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00")), 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        // Act
        await sut.ForceRefreshAsync();

        // Assert
        Assert.Single(publishedSnapshots);
        Assert.Equal(50, publishedSnapshots[0].Limits[LimitKind.Session5h].Pct);
        Assert.NotNull(publishedSnapshots[0].Limits[LimitKind.Session5h].ResetsAt);
    }

    [Fact]
    public async Task Poller_CurrentRetainsLatestSnapshot()
    {
        // Arrange
        var publishedSnapshots = new List<UsageSnapshot>();
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(
            new UsageSnapshot(
                BuildLimitState(75, "2026-06-19T18:00:00+00:00", true),
                DateTimeOffset.UtcNow, null, false)));
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var sut = new Poller(fakeClient, clock, 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        // Act
        await sut.ForceRefreshAsync();

        // Assert
        Assert.NotNull(sut.Current);
        Assert.Equal(75, sut.Current.Limits[LimitKind.Session5h].Pct);
    }

    #endregion

    #region Stale handling

    [Fact]
    public async Task Poller_StaleSnapshot_RetainsLastGoodValues()
    {
        // Arrange
        var publishedSnapshots = new List<UsageSnapshot>();

        var t0 = DateTimeOffset.Parse("2026-06-18T14:00:00+00:00");
        var t1 = DateTimeOffset.Parse("2026-06-18T14:03:00+00:00");

        var firstSnapshot = new UsageSnapshot(
            BuildLimitState(42, "2026-06-19T18:00:00+00:00", true),
            t0, null, false);

        var staleSnapshot = new UsageSnapshot(
            BuildLimitState(null, null, false),
            t1, "HTTP 429 Too Many Requests", true,
            ErrorKind: ErrorKind.RateLimited, RetryAfterSeconds: 300);

        var fetchCount = 0;
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(fetchCount++ == 0 ? firstSnapshot : staleSnapshot));

        var clock = new FakeClock(t0);
        var sut = new Poller(fakeClient, clock, 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        // Act — first fetch publishes good snapshot
        await sut.ForceRefreshAsync();
        Assert.Equal(42, publishedSnapshots[0].Limits[LimitKind.Session5h].Pct);

        // Act — second fetch publishes stale snapshot with last-good retained
        await sut.ForceRefreshAsync();

        // Assert
        Assert.Equal(2, publishedSnapshots.Count);
        Assert.True(publishedSnapshots[1].Stale);
        Assert.NotNull(publishedSnapshots[1].Error);
        // Last-good values retained: Session5h pct 42 from first snapshot
        Assert.Equal(42, publishedSnapshots[1].Limits[LimitKind.Session5h].Pct);
        // A7.1: UpdatedAt keeps last-good timestamp, carries ErrorKind + RetryAfterSeconds
        Assert.Equal(t0, publishedSnapshots[1].UpdatedAt);
        Assert.Equal(ErrorKind.RateLimited, publishedSnapshots[1].ErrorKind);
        Assert.Equal(300, publishedSnapshots[1].RetryAfterSeconds);
    }

    [Fact]
    public async Task Poller_UsesConfiguredInterval()
    {
        // Arrange
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(
            new UsageSnapshot(
                BuildLimitState(50, "2026-06-19T18:00:00+00:00", true),
                DateTimeOffset.UtcNow, null, false)));
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var sut = new Poller(fakeClient, clock, 10); // 10s interval

        // Act
        await sut.ForceRefreshAsync();

        // Assert
        Assert.NotNull(sut.Current);
    }

    [Fact]
    public async Task Poller_NoLastGoodWithFailure_PublishesErrorKindStale() // A7.2
    {
        // Arrange — no prior snapshot, fetch fails
        var publishedSnapshots = new List<UsageSnapshot>();
        var failedSnapshot = new UsageSnapshot(
            BuildLimitState(null, null, false),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"),
            "HTTP 429 Too Many Requests", true,
            ErrorKind: ErrorKind.RateLimited);

        var fakeClient = new FakeUsageClient(_ => Task.FromResult(failedSnapshot));
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var sut = new Poller(fakeClient, clock, 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        // Act
        await sut.ForceRefreshAsync();

        // Assert — published snapshot carries ErrorKind + Stale, no merge
        Assert.Single(publishedSnapshots);
        Assert.True(publishedSnapshots[0].Stale);
        Assert.Equal(ErrorKind.RateLimited, publishedSnapshots[0].ErrorKind);
    }

    [Fact]
    public async Task Poller_SuccessfulFetch_ClearsErrorKind() // A7.3
    {
        // Arrange — first fetch fails, second succeeds
        var publishedSnapshots = new List<UsageSnapshot>();
        var failSnap = new UsageSnapshot(
            BuildLimitState(null, null, false),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"),
            "HTTP 429", true,
            ErrorKind: ErrorKind.RateLimited);
        var successSnap = new UsageSnapshot(
            BuildLimitState(30, "2026-06-19T18:00:00+00:00", true),
            DateTimeOffset.Parse("2026-06-18T14:03:00+00:00"), null, false);

        var fetchCount = 0;
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(
            fetchCount++ == 0 ? failSnap : successSnap));
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var sut = new Poller(fakeClient, clock, 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        // Act — fail then succeed
        await sut.ForceRefreshAsync();
        await sut.ForceRefreshAsync();

        // Assert — success clears ErrorKind, Stale=false
        Assert.Equal(2, publishedSnapshots.Count);
        Assert.Null(publishedSnapshots[1].ErrorKind);
        Assert.False(publishedSnapshots[1].Stale);
        Assert.Null(publishedSnapshots[1].Error);
    }

    #endregion

    #region Consecutive failures + back-off (A8.2–A8.8)

    [Fact] // A8.2 — success snapshots have ConsecutiveFailures == 0
    public async Task Counter_Success_KeepsZero()
    {
        var publishedSnapshots = new List<UsageSnapshot>();
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(
            new UsageSnapshot(
                BuildLimitState(50, "2026-06-19T18:00:00+00:00", true),
                DateTimeOffset.UtcNow, null, false)));
        var sut = new Poller(fakeClient, new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00")), 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        await sut.ForceRefreshAsync();
        await sut.ForceRefreshAsync();

        Assert.Equal(2, publishedSnapshots.Count);
        Assert.Equal(0, publishedSnapshots[0].ConsecutiveFailures);
        Assert.Equal(0, publishedSnapshots[1].ConsecutiveFailures);
    }

    [Fact] // A8.3 — first failure → ConsecutiveFailures == 1
    public async Task Counter_FirstFailure_IsOne()
    {
        var publishedSnapshots = new List<UsageSnapshot>();
        var failSnap = new UsageSnapshot(
            BuildLimitState(null, null, false),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"),
            "HTTP 429", true,
            ErrorKind: ErrorKind.RateLimited);
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(failSnap));
        var sut = new Poller(fakeClient, new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00")), 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        await sut.ForceRefreshAsync();

        Assert.Single(publishedSnapshots);
        Assert.Equal(ErrorKind.RateLimited, publishedSnapshots[0].ErrorKind);
        Assert.Equal(1, publishedSnapshots[0].ConsecutiveFailures);
    }

    [Fact] // A8.4 — two failures → ConsecutiveFailures == 2
    public async Task Counter_TwoFailures_IsTwo()
    {
        var publishedSnapshots = new List<UsageSnapshot>();
        var failSnap = new UsageSnapshot(
            BuildLimitState(null, null, false),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"),
            "HTTP 429", true,
            ErrorKind: ErrorKind.RateLimited);
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(failSnap));
        var sut = new Poller(fakeClient, new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00")), 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        await sut.ForceRefreshAsync();
        await sut.ForceRefreshAsync();

        Assert.Equal(2, publishedSnapshots.Count);
        Assert.Equal(2, publishedSnapshots[1].ConsecutiveFailures);
    }

    [Fact] // A8.5 — failure then success → reset to 0
    public async Task Counter_ResetsOnSuccess()
    {
        var publishedSnapshots = new List<UsageSnapshot>();
        var failSnap = new UsageSnapshot(
            BuildLimitState(null, null, false),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"),
            "HTTP 429", true,
            ErrorKind: ErrorKind.RateLimited);
        var okSnap = new UsageSnapshot(
            BuildLimitState(30, "2026-06-19T18:00:00+00:00", true),
            DateTimeOffset.Parse("2026-06-18T14:03:00+00:00"), null, false);

        var fetchCount = 0;
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(
            fetchCount++ == 0 ? failSnap : okSnap));
        var sut = new Poller(fakeClient, new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00")), 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        await sut.ForceRefreshAsync();
        await sut.ForceRefreshAsync();

        Assert.Equal(2, publishedSnapshots.Count);
        Assert.Equal(1, publishedSnapshots[0].ConsecutiveFailures);
        Assert.Equal(0, publishedSnapshots[1].ConsecutiveFailures);
    }

    [Fact] // A8.6 — back-off wiring: CliRequiredForReauth n=1 → 15s
    public async Task BackOff_CliReauth_FirstRetryIs15()
    {
        var reauthSnap = new UsageSnapshot(
            BuildLimitState(null, null, false),
            DateTimeOffset.Parse("2026-07-14T00:00:00Z"),
            "e", true, ErrorKind.CliRequiredForReauth);
        var client = new FakeUsageClient(_ => Task.FromResult(reauthSnap));
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-14T00:00:00Z"));
        using var cts = new CancellationTokenSource();
        var gate = new TaskCompletionSource();
        int captured = -1;
        var sut = new Poller(client, clock, 180, new NullLog(),
            (s, ct) => { captured = s; gate.TrySetResult(); cts.Cancel(); return Task.CompletedTask; });
        await sut.StartAsync(cts.Token);
        await gate.Task;
        Assert.Equal(15, captured); // §6, n=1 → 15
    }

    [Fact] // A8.7 — back-off wiring: RateLimited RetryAfter=0 → 60s floor
    public async Task BackOff_RateLimited_RetryAfterZero_Is60()
    {
        var rateSnap = new UsageSnapshot(
            BuildLimitState(null, null, false),
            DateTimeOffset.Parse("2026-07-14T00:00:00Z"),
            "429", true, ErrorKind.RateLimited, RetryAfterSeconds: 0);
        var client = new FakeUsageClient(_ => Task.FromResult(rateSnap));
        var clock = new FakeClock(DateTimeOffset.Parse("2026-07-14T00:00:00Z"));
        using var cts = new CancellationTokenSource();
        var gate = new TaskCompletionSource();
        int captured = -1;
        var sut = new Poller(client, clock, 180, new NullLog(),
            (s, ct) => { captured = s; gate.TrySetResult(); cts.Cancel(); return Task.CompletedTask; });
        await sut.StartAsync(cts.Token);
        await gate.Task;
        Assert.Equal(60, captured); // §6 floor
    }

    [Fact] // A8.8 — extended stale test: ConsecutiveFailures == 1 on merged stale
    public async Task Poller_StaleSnapshot_RetainsLastGoodValues_WithCounter()
    {
        var publishedSnapshots = new List<UsageSnapshot>();

        var t0 = DateTimeOffset.Parse("2026-06-18T14:00:00+00:00");
        var t1 = DateTimeOffset.Parse("2026-06-18T14:03:00+00:00");

        var firstSnapshot = new UsageSnapshot(
            BuildLimitState(42, "2026-06-19T18:00:00+00:00", true),
            t0, null, false);

        var staleSnapshot = new UsageSnapshot(
            BuildLimitState(null, null, false),
            t1, "HTTP 429 Too Many Requests", true,
            ErrorKind: ErrorKind.RateLimited, RetryAfterSeconds: 300);

        var fetchCount = 0;
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(fetchCount++ == 0 ? firstSnapshot : staleSnapshot));

        var clock = new FakeClock(t0);
        var sut = new Poller(fakeClient, clock, 180);
        sut.SnapshotPublished += s => publishedSnapshots.Add(s);

        await sut.ForceRefreshAsync();
        await sut.ForceRefreshAsync();

        Assert.Equal(2, publishedSnapshots.Count);
        Assert.True(publishedSnapshots[1].Stale);
        Assert.Equal(42, publishedSnapshots[1].Limits[LimitKind.Session5h].Pct);
        Assert.Equal(t0, publishedSnapshots[1].UpdatedAt);
        Assert.Equal(ErrorKind.RateLimited, publishedSnapshots[1].ErrorKind);
        Assert.Equal(300, publishedSnapshots[1].RetryAfterSeconds);
        // A8.8: extended assertion — stale snapshot has ConsecutiveFailures == 1
        Assert.Equal(1, publishedSnapshots[1].ConsecutiveFailures);
    }

    #endregion

    #region Helper

    private static IReadOnlyDictionary<LimitKind, LimitState> BuildLimitState(int? pct, string? resetsAtStr, bool present)
    {
        var dict = new Dictionary<LimitKind, LimitState>();
        foreach (var kind in LimitKindMapping.AllKinds)
        {
            if (present)
            {
                var resetsAt = resetsAtStr != null ? DateTimeOffset.Parse(resetsAtStr) : (DateTimeOffset?)null;
                dict[kind] = new LimitState(pct, resetsAt, present);
            }
            else
            {
                dict[kind] = new LimitState(null, null, false);
            }
        }
        return dict;
    }

    #endregion
}

/// <summary>
/// Fake IUsageClient for Poller tests.
/// </summary>
public class FakeUsageClient : IUsageClient
{
    private readonly Func<CancellationToken, Task<UsageSnapshot>> _factory;

    public FakeUsageClient(Func<CancellationToken, Task<UsageSnapshot>> factory)
    {
        _factory = factory;
    }

    public Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        return _factory(ct);
    }
}
