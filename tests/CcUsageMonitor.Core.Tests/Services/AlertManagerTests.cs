using System;
using System.Collections.Generic;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using CcUsageMonitor.Core.Tests.Fakes;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class AlertManagerTests
{
    /// <summary>
    /// Creates limits focused on WeeklyAll. Session5h is always below threshold (80)
    /// so only WeeklyAll triggers alerts in these tests.
    /// </summary>
    private static IReadOnlyDictionary<LimitKind, LimitState> WeeklyAllLimits(
        int? weeklyAllPct, string? weeklyAllResetsAt,
        int? session5hPct = 80)
    {
        var dict = new Dictionary<LimitKind, LimitState>();
        dict[LimitKind.Session5h] = new LimitState(session5hPct, DateTimeOffset.Parse("2026-06-19T18:00:00+00:00"), true);
        dict[LimitKind.WeeklyAll] = new LimitState(weeklyAllPct, weeklyAllResetsAt != null ? DateTimeOffset.Parse(weeklyAllResetsAt) : null, true);
        dict[LimitKind.WeeklySonnet] = new LimitState(null, null, false);
        dict[LimitKind.WeeklyOpus] = new LimitState(null, null, false);
        return dict;
    }

    #region Basic alerting

    [Fact]
    public void AlertManager_OnSnapshot_FiresOnceOnThresholdCrossing()
    {
        // Arrange
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // Act — 90% crosses alert threshold
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(90, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));

        // Assert
        Assert.Single(sink.ShowCalls);
        Assert.Equal("Claude usage at 90%", sink.ShowCalls[0].Title);
        Assert.Equal("Weekly (all models) resets in 28h 0m", sink.ShowCalls[0].Body);
    }

    [Fact]
    public void AlertManager_OnSnapshot_NoFireBelowThreshold()
    {
        // Arrange
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // Act
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(89, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));

        // Assert
        Assert.Empty(sink.ShowCalls);
    }

    [Fact]
    public void AlertManager_OnSnapshot_DoesNotReFireSameResetsAt()
    {
        // Arrange — the spec's key edge: drop-below-then-rise-above with same ResetsAt → no second toast
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // First: 95% fires
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));
        Assert.Single(sink.ShowCalls);

        // Drop to 80%
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(80, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:01:00+00:00"), null, false));

        // Rise back to 95% with SAME ResetsAt → no second toast
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:02:00+00:00"), null, false));

        // Assert — still only one toast
        Assert.Single(sink.ShowCalls);
    }

    [Fact]
    public void AlertManager_OnSnapshot_ReArmsOnStrictlyGreaterResetsAt()
    {
        // Arrange — two non-null ResetsAt require strictly greater for re-arm
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // First: 95% at T1
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));
        Assert.Single(sink.ShowCalls);

        // Drop and rise back with LATER ResetsAt → re-arm and fire again
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(80, "2026-06-19T20:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:01:00+00:00"), null, false));

        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, "2026-06-19T20:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:02:00+00:00"), null, false));

        // Assert — two toasts now
        Assert.Equal(2, sink.ShowCalls.Count);
    }

    #endregion

    #region Null resetsAt

    [Fact]
    public void AlertManager_OnSnapshot_NullResetsAt_FiresButDoesNotReFire()
    {
        // Arrange — null reset crossing records as fired, no re-fire each poll
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // First: 95% with null ResetsAt
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, null),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));
        Assert.Single(sink.ShowCalls);

        // Second: still 95% with null ResetsAt → no re-fire
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, null),
            DateTimeOffset.Parse("2026-06-18T14:01:00+00:00"), null, false));

        // Assert — still only one toast
        Assert.Single(sink.ShowCalls);
    }

    [Fact]
    public void AlertManager_OnSnapshot_RecordedNullThenNonNull_ResetsAt_ReArms()
    {
        // Arrange — recorded-null then non-null ResetsAt → re-arms (per spec §4.6 predicate)
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // First: 95% with null ResetsAt → fires
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, null),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));
        Assert.Single(sink.ShowCalls);

        // Second: 95% with non-null ResetsAt → re-arms and fires again
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:01:00+00:00"), null, false));

        // Assert — two toasts (first from null, second from re-arm)
        Assert.Equal(2, sink.ShowCalls.Count);
    }

    #endregion

    #region Unavailable sink

    [Fact]
    public void AlertManager_OnSnapshot_UnavailableSink_RecordsAsFired()
    {
        // Arrange — unavailable sink still records as fired (no re-fire)
        var sink = new FakeNotificationSink();
        sink.Available = false;
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // First: 95% → sink unavailable, recorded as fired
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, null),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));
        Assert.Empty(sink.ShowCalls); // sink unavailable

        // Second: still 95% → no re-fire even though pct still >= threshold
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, null),
            DateTimeOffset.Parse("2026-06-18T14:01:00+00:00"), null, false));

        // Assert — still no toasts (fired was recorded despite unavailable sink)
        Assert.Empty(sink.ShowCalls);
    }

    #endregion

    #region Enabled flag

    [Fact]
    public void AlertManager_Disabled_DoesNotFire()
    {
        // Arrange
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);
        alertManager.AlertsEnabled = false;

        // Act
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));

        // Assert
        Assert.Empty(sink.ShowCalls);
    }

    #endregion

    #region Toast content — exact literals

    [Fact]
    public void AlertManager_OnSnapshot_FutureResetsAt_ToastBodyExact()
    {
        // Arrange — future reset with known countdown
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // Act
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(90, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));

        // Assert — exact toast body for future reset
        Assert.Single(sink.ShowCalls);
        Assert.Equal("Weekly (all models) resets in 28h 0m", sink.ShowCalls[0].Body);
    }

    [Fact]
    public void AlertManager_OnSnapshot_PastResetsAt_ToastBodyExact()
    {
        // Arrange — past reset → "is resetting"
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T16:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // Act
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, "2026-06-18T14:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T16:00:00+00:00"), null, false));

        // Assert
        Assert.Single(sink.ShowCalls);
        Assert.Equal("Weekly (all models) is resetting", sink.ShowCalls[0].Body);
        Assert.DoesNotContain("resets in", sink.ShowCalls[0].Body);
    }

    [Fact]
    public void AlertManager_OnSnapshot_NullResetsAt_ToastBodyExact()
    {
        // Arrange — null reset → just the label
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // Act
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(95, null),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));

        // Assert
        Assert.Single(sink.ShowCalls);
        Assert.Equal("Weekly (all models)", sink.ShowCalls[0].Body);
        Assert.DoesNotContain("resets in", sink.ShowCalls[0].Body);
    }

    [Fact]
    public void AlertManager_OnSnapshot_TitleExact()
    {
        // Arrange
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // Act
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(90, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));

        // Assert
        Assert.Equal("Claude usage at 90%", sink.ShowCalls[0].Title);
    }

    [Fact]
    public void AlertManager_OnSnapshot_UnclampedPct_TitleExact()
    {
        // Arrange
        var sink = new FakeNotificationSink();
        var clock = new FakeClock(DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"));
        var alertManager = new AlertManager(sink, clock, 90, 70);

        // Act — 103% (unclamped)
        alertManager.OnSnapshot(new UsageSnapshot(
            WeeklyAllLimits(103, "2026-06-19T18:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-18T14:00:00+00:00"), null, false));

        // Assert
        Assert.Equal("Claude usage at 103%", sink.ShowCalls[0].Title);
    }

    #endregion
}
