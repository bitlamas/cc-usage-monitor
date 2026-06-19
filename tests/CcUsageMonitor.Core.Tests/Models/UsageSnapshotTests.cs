using CcUsageMonitor.Core.Models;
using System;
using System.Collections.Generic;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Models;

public class UsageSnapshotTests
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 6, 18, 14, 5, 0, TimeSpan.Zero);

    [Fact]
    public void UsageSnapshot_RecordsLimits_UpdatedAt_Error_Stale()
    {
        var limits = new Dictionary<LimitKind, LimitState>
        {
            { LimitKind.Session5h, new LimitState(42, new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero), true) },
            { LimitKind.WeeklyAll, new LimitState(80, new DateTimeOffset(2026, 6, 21, 9, 0, 0, TimeSpan.Zero), true) }
        };
        var snapshot = new UsageSnapshot(limits, UpdatedAt, null, false);
        Assert.Equal(limits, snapshot.Limits);
        Assert.Equal(UpdatedAt, snapshot.UpdatedAt);
        Assert.Null(snapshot.Error);
        Assert.False(snapshot.Stale);
    }

    [Fact]
    public void UsageSnapshot_StaleWithErrorMessage()
    {
        var limits = new Dictionary<LimitKind, LimitState>
        {
            { LimitKind.Session5h, new LimitState(null, null, false) }
        };
        var snapshot = new UsageSnapshot(limits, UpdatedAt, "HTTP 401 after refresh", true);
        Assert.Equal("HTTP 401 after refresh", snapshot.Error);
        Assert.True(snapshot.Stale);
    }

    [Fact]
    public void UsageSnapshot_AllLimitKindsPresent()
    {
        var limits = new Dictionary<LimitKind, LimitState>();
        foreach (LimitKind kind in Enum.GetValues<LimitKind>())
        {
            limits[kind] = new LimitState(50, new DateTimeOffset(2026, 6, 21, 9, 0, 0, TimeSpan.Zero), true);
        }
        var snapshot = new UsageSnapshot(limits, UpdatedAt, null, false);
        Assert.Equal(4, snapshot.Limits.Count);
    }

    [Fact]
    public void UsageSnapshot_MixedPresentFlags()
    {
        var limits = new Dictionary<LimitKind, LimitState>
        {
            { LimitKind.Session5h, new LimitState(42, new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero), true) },
            { LimitKind.WeeklyOpus, new LimitState(null, null, false) }
        };
        var snapshot = new UsageSnapshot(limits, UpdatedAt, null, false);
        Assert.True(snapshot.Limits[LimitKind.Session5h].Present);
        Assert.False(snapshot.Limits[LimitKind.WeeklyOpus].Present);
    }

    [Fact]
    public void UsageSnapshot_NullError_Healthy()
    {
        var limits = new Dictionary<LimitKind, LimitState>
        {
            { LimitKind.Session5h, new LimitState(75, new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero), true) }
        };
        var snapshot = new UsageSnapshot(limits, UpdatedAt, null, false);
        Assert.Null(snapshot.Error);
    }

    [Fact]
    public void UsageSnapshot_ValueEquality()
    {
        var dt = new DateTimeOffset(2026, 6, 18, 14, 5, 0, TimeSpan.Zero);
        var limits = new Dictionary<LimitKind, LimitState>
        {
            { LimitKind.Session5h, new LimitState(42, dt, true) }
        };
        var s1 = new UsageSnapshot(limits, dt, null, false);
        var s2 = new UsageSnapshot(limits, dt, null, false);
        Assert.Equal(s1, s2);
    }
}
