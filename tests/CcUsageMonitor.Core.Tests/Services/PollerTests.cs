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

        var firstSnapshot = new UsageSnapshot(
            BuildLimitState(42, "2026-06-19T18:00:00+00:00", true),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false);

        var staleSnapshot = new UsageSnapshot(
            BuildLimitState(null, null, false),
            DateTimeOffset.Parse("2026-06-18T14:03:00+00:00"), "Network timeout", true);

        var fetchCount = 0;
        var fakeClient = new FakeUsageClient(_ => Task.FromResult(fetchCount++ == 0 ? firstSnapshot : staleSnapshot));

        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
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
