using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class UsageTextTests
{
    // Fixed local time zone for deterministic testing
    private static readonly TimeSpan LocalOffset = TimeSpan.Zero; // UTC for test determinism
    private static readonly DateTime FixedNowUtc = new(2026, 6, 18, 14, 5, 0, DateTimeKind.Utc);

    private static DateTimeOffset Now => new(FixedNowUtc, LocalOffset);
    private static DateTimeOffset UpdatedAt => new(2026, 6, 18, 14, 5, 0, LocalOffset);

    [Theory]
    [InlineData(16, 5, 0, "2h 0m")]       // exact hour
    [InlineData(15, 35, 45, "1h 30m")]   // floor exercised (45s dropped)
    [InlineData(14, 52, 0, "47m")]        // sub-hour
    [InlineData(14, 5, 30, "0m")]          // sub-minute: 0 < remaining < 1m → "0m"
    [InlineData(14, 5, 0, "resetting…")] // ResetsAt == now → sentinel
    [InlineData(13, 0, 0, "resetting…")] // past reset → sentinel
    public void UsageText_Countdown_ResetsAt_Correct(int rH, int rM, int rS, string expected)
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, rH, rM, rS, LocalOffset);
        var countdown = UsageText.Countdown(resetsAt, Now);
        Assert.Equal(expected, countdown);
    }

    [Fact]
    public void UsageText_Countdown_NullResetsAt_ReturnsNull()
    {
        Assert.Null(UsageText.Countdown(null, Now));
    }

    // Tooltip fixtures from spec §6.2
    [Fact]
    public void UsageText_Tooltip_FutureExactHour()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 16, 5, 0, LocalOffset);
        var state = new LimitState(42, resetsAt, true);
        var label = LimitKindMapping.GetLabel(LimitKind.Session5h);
        var expected = $"5-hour session — 42% · resets in 2h 0m · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.Session5h, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.Contains("—", actual);
        Assert.Contains("·", actual);
    }

    [Fact]
    public void UsageText_Tooltip_FutureFloor()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 15, 35, 45, LocalOffset);
        var state = new LimitState(80, resetsAt, true);
        var expected = $"5-hour session — 80% · resets in 1h 30m · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.Session5h, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_SubHour()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 52, 0, LocalOffset);
        var state = new LimitState(95, resetsAt, true);
        var expected = $"5-hour session — 95% · resets in 47m · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.Session5h, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_SubMinute()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 5, 30, LocalOffset);
        var state = new LimitState(95, resetsAt, true);
        var expected = $"5-hour session — 95% · resets in 0m · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.Session5h, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_ResetNow_Sentinel()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 5, 0, LocalOffset);
        var state = new LimitState(75, resetsAt, true);
        var expected = $"Weekly (all models) — 75% · resetting… · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    [Fact]
    public void UsageText_Tooltip_PastReset_Sentinel()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 13, 0, 0, LocalOffset);
        var state = new LimitState(75, resetsAt, true);
        var expected = $"Weekly (all models) — 75% · resetting… · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    [Fact]
    public void UsageText_Tooltip_NullReset_SegmentOmitted()
    {
        var state = new LimitState(25, null, true);
        var expected = $"Weekly (Sonnet) — 25% · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.WeeklySonnet, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    [Fact]
    public void UsageText_Tooltip_NullPct_FutureReset()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 17, 5, 0, LocalOffset);
        var state = new LimitState(null, resetsAt, true);
        var expected = $"Weekly (all models) — --% · resets in 3h 0m · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_NullPct_NullReset()
    {
        var state = new LimitState(null, null, true);
        var expected = $"Weekly (Sonnet) — --% · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.WeeklySonnet, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    // Toast fixtures from spec §6.3
    [Fact]
    public void UsageText_ToastTitle_Pct()
    {
        Assert.Equal("Claude usage at 90%", UsageText.ToastTitle(90));
    }

    [Fact]
    public void UsageText_ToastTitle_UnclampedPct()
    {
        Assert.Equal("Claude usage at 103%", UsageText.ToastTitle(103));
    }

    [Fact]
    public void UsageText_ToastBody_FutureReset()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 16, 18, 0, LocalOffset);
        var body = UsageText.ToastBody(LimitKind.WeeklyAll, resetsAt, Now);
        Assert.Equal("Weekly (all models) resets in 2h 13m", body);
    }

    [Fact]
    public void UsageText_ToastBody_PastReset()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 0, 0, LocalOffset);
        var body = UsageText.ToastBody(LimitKind.WeeklyAll, resetsAt, Now);
        Assert.Equal("Weekly (all models) is resetting", body);
        Assert.DoesNotContain("resets in", body);
    }

    [Fact]
    public void UsageText_ToastBody_NullReset()
    {
        var body = UsageText.ToastBody(LimitKind.WeeklyAll, null, Now);
        Assert.Equal("Weekly (all models)", body);
        Assert.DoesNotContain("resets in", body);
    }

    // Error tooltips from spec §7
    [Fact]
    public void UsageText_ErrorTooltip_NotLoggedIn()
    {
        Assert.Equal("Not logged in to Claude Code", UsageText.ErrorTooltip(ErrorKind.NotLoggedIn));
    }

    [Fact]
    public void UsageText_ErrorTooltip_CliRequiredForReauth()
    {
        var result = UsageText.ErrorTooltip(ErrorKind.CliRequiredForReauth);
        Assert.Contains("Claude Code CLI", result);
        Assert.Contains("re-auth", result);
    }

    // Edge: null-pct + reset-now sentinel (independent combinations)
    [Fact]
    public void UsageText_Tooltip_NullPct_ResetNow_Sentinel()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 5, 0, LocalOffset);
        var state = new LimitState(null, resetsAt, true);
        var expected = $"Weekly (all models) — --% · resetting… · updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    // Edge: exact-hour with 0 minutes
    [Fact]
    public void UsageText_Countdown_ExactHour()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 15, 5, 0, LocalOffset);
        var result = UsageText.Countdown(resetsAt, Now);
        Assert.Equal("1h 0m", result);
    }
}
