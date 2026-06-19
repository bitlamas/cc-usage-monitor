using CcUsageMonitor.Core.Models;
using System;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Models;

public class LimitStateTests
{
    [Fact]
    public void LimitState_RecordsPct_ResetsAt_Present()
    {
        var state = new LimitState(42, new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero), true);
        Assert.Equal(42, state.Pct);
        Assert.Equal(new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero), state.ResetsAt);
        Assert.True(state.Present);
    }

    [Fact]
    public void LimitState_NullPct_Present()
    {
        var state = new LimitState(null, new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero), true);
        Assert.Null(state.Pct);
    }

    [Fact]
    public void LimitState_NullResetsAt_Present()
    {
        var state = new LimitState(25, null, true);
        Assert.Null(state.ResetsAt);
    }

    [Fact]
    public void LimitState_NotPresent()
    {
        var state = new LimitState(null, null, false);
        Assert.False(state.Present);
        Assert.Null(state.Pct);
        Assert.Null(state.ResetsAt);
    }

    [Fact]
    public void LimitState_ClampedPct_Allowed()
    {
        // Pct is unclamped — values >100 are allowed for display
        var state = new LimitState(125, new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero), true);
        Assert.Equal(125, state.Pct);
    }

    [Fact]
    public void LimitState_ValueEquality()
    {
        var dt = new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero);
        var s1 = new LimitState(42, dt, true);
        var s2 = new LimitState(42, dt, true);
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void LimitState_DifferentPct_NotEqual()
    {
        var dt = new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero);
        var s1 = new LimitState(42, dt, true);
        var s2 = new LimitState(43, dt, true);
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void LimitState_DifferentPresent_NotEqual()
    {
        var dt = new DateTimeOffset(2026, 6, 18, 16, 5, 0, TimeSpan.Zero);
        var s1 = new LimitState(42, dt, true);
        var s2 = new LimitState(42, dt, false);
        Assert.NotEqual(s1, s2);
    }
}
