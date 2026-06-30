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
