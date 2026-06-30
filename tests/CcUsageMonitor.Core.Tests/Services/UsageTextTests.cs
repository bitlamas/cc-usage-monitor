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
        var expected = "5-hour · 42% · Resets in 2h 0m";
        var actual = UsageText.Tooltip(LimitKind.Session5h, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_FutureFloor()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 15, 35, 45, LocalOffset);
        var state = new LimitState(80, resetsAt, true);
        var expected = "5-hour · 80% · Resets in 1h 30m";
        var actual = UsageText.Tooltip(LimitKind.Session5h, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_SubHour()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 52, 0, LocalOffset);
        var state = new LimitState(95, resetsAt, true);
        var expected = "5-hour · 95% · Resets in 47m";
        var actual = UsageText.Tooltip(LimitKind.Session5h, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_SubMinute()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 5, 30, LocalOffset);
        var state = new LimitState(95, resetsAt, true);
        var expected = "5-hour · 95% · Resets in 0m";
        var actual = UsageText.Tooltip(LimitKind.Session5h, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_ResetNow_Sentinel()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 5, 0, LocalOffset);
        var state = new LimitState(75, resetsAt, true);
        var expected = "Weekly · 75% · Resetting";
        var actual = UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    [Fact]
    public void UsageText_Tooltip_PastReset_Sentinel()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 13, 0, 0, LocalOffset);
        var state = new LimitState(75, resetsAt, true);
        var expected = "Weekly · 75% · Resetting";
        var actual = UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    [Fact]
    public void UsageText_Tooltip_NullReset_ShowsUpdated()
    {
        var state = new LimitState(25, null, true);
        var expected = "Weekly (Sonnet) · 25% · Updated 14:05";
        var actual = UsageText.Tooltip(LimitKind.WeeklySonnet, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    [Fact]
    public void UsageText_Tooltip_NullPct_FutureReset()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 17, 5, 0, LocalOffset);
        var state = new LimitState(null, resetsAt, true);
        var expected = "Weekly · --% · Resets in 3h 0m";
        var actual = UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsageText_Tooltip_NullPct_NullReset()
    {
        var state = new LimitState(null, null, true);
        var expected = "Weekly (Sonnet) · --% · Updated 14:05";
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
        Assert.Equal("Claude Code CLI required for re-auth", UsageText.ErrorTooltip(ErrorKind.CliRequiredForReauth));
    }

    // Edge: null-pct + reset-now sentinel (independent combinations)
    [Fact]
    public void UsageText_Tooltip_NullPct_ResetNow_Sentinel()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 14, 5, 0, LocalOffset);
        var state = new LimitState(null, resetsAt, true);
        var expected = "Weekly · --% · Resetting";
        var actual = UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("resets in", actual);
    }

    // Weekly far reset (>= 24h) → absolute abbreviated day + 12h time
    [Fact]
    public void UsageText_Tooltip_WeeklyFarReset_AbsoluteDay()
    {
        // now = Thu 2026-06-18 14:05; reset Sun 2026-06-21 10:00 (>24h away)
        var resetsAt = new DateTimeOffset(2026, 6, 21, 10, 0, 0, LocalOffset);
        var state = new LimitState(46, resetsAt, true);
        Assert.Equal("Weekly · 46% · Resets Sun 10AM",
            UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now));
    }

    [Fact]
    public void UsageText_Tooltip_WeeklyFarReset_WithMinutes()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 21, 22, 30, 0, LocalOffset); // Sun 10:30 PM
        var state = new LimitState(46, resetsAt, true);
        Assert.Equal("Weekly · 46% · Resets Sun 10:30PM",
            UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now));
    }

    [Fact]
    public void UsageText_Tooltip_JustUnder24h_UsesRelative()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 19, 13, 0, 0, LocalOffset); // 22h 55m away (<24h)
        var state = new LimitState(46, resetsAt, true);
        Assert.Equal("Weekly · 46% · Resets in 22h 55m",
            UsageText.Tooltip(LimitKind.WeeklyAll, state, UpdatedAt, Now));
    }

    // Edge: exact-hour with 0 minutes
    [Fact]
    public void UsageText_Countdown_ExactHour()
    {
        var resetsAt = new DateTimeOffset(2026, 6, 18, 15, 5, 0, LocalOffset);
        var result = UsageText.Countdown(resetsAt, Now);
        Assert.Equal("1h 0m", result);
    }

    // §8.1 — TrayTooltip tests
    [Fact]
    public void TrayTooltip_RateLimited_IsSilent() // A8.1 — the bug case
    {
        var now = DateTimeOffset.Parse("2026-06-30T09:00:00-04:00");
        var state = new LimitState(0, now.AddHours(4).AddMinutes(51), Present: true);
        Assert.Equal("5-hour · 0% · Resets in 4h 51m",
            UsageText.TrayTooltip(LimitKind.Session5h, state, now, now, ErrorKind.RateLimited));
    }

    [Fact]
    public void TrayTooltip_NetworkError_IsSilent() // A8.2
    {
        var now = DateTimeOffset.Parse("2026-06-30T09:00:00-04:00");
        var state = new LimitState(0, now.AddHours(4).AddMinutes(51), Present: true);
        Assert.Equal("5-hour · 0% · Resets in 4h 51m",
            UsageText.TrayTooltip(LimitKind.Session5h, state, now, now, ErrorKind.NetworkError));
    }

    [Fact]
    public void TrayTooltip_ServerError_IsSilent() // A8.3
    {
        var now = DateTimeOffset.Parse("2026-06-30T09:00:00-04:00");
        var state = new LimitState(0, now.AddHours(4).AddMinutes(51), Present: true);
        Assert.Equal("5-hour · 0% · Resets in 4h 51m",
            UsageText.TrayTooltip(LimitKind.Session5h, state, now, now, ErrorKind.ServerError));
    }

    [Fact]
    public void TrayTooltip_NullErrorKind_IsSilent() // A8.4
    {
        var now = DateTimeOffset.Parse("2026-06-30T09:00:00-04:00");
        var state = new LimitState(0, now.AddHours(4).AddMinutes(51), Present: true);
        Assert.Equal("5-hour · 0% · Resets in 4h 51m",
            UsageText.TrayTooltip(LimitKind.Session5h, state, now, now, null));
    }

    [Fact]
    public void TrayTooltip_NotLoggedIn_AppendsError() // A8.5
    {
        var now = DateTimeOffset.Parse("2026-06-30T03:20:00-04:00");
        var state = new LimitState(null, null, Present: false);
        Assert.Equal("5-hour · --% · Updated 03:20 — Not logged in to Claude Code",
            UsageText.TrayTooltip(LimitKind.Session5h, state, now, now, ErrorKind.NotLoggedIn));
    }

    [Fact]
    public void TrayTooltip_CliRequiredForReauth_AppendsError() // A8.6
    {
        var now = DateTimeOffset.Parse("2026-06-30T03:20:00-04:00");
        var state = new LimitState(null, null, Present: false);
        Assert.Equal("5-hour · --% · Updated 03:20 — Claude Code CLI required for re-auth",
            UsageText.TrayTooltip(LimitKind.Session5h, state, now, now, ErrorKind.CliRequiredForReauth));
    }

    [Fact]
    public void TrayTooltip_EmDashIsUnicode() // A8.7
    {
        var now = DateTimeOffset.Parse("2026-06-30T03:20:00-04:00");
        var state = new LimitState(null, null, Present: false);
        var actual = UsageText.TrayTooltip(LimitKind.Session5h, state, now, now, ErrorKind.NotLoggedIn);
        Assert.Contains("\u2014", actual); // U+2014 EmDash, not ASCII hyphen
    }

    // §8.2 — FlyoutErrorLine tests
    [Fact]
    public void FlyoutErrorLine_RateLimited_ReturnsNull() // A8.10 — the bug case
    {
        Assert.Null(UsageText.FlyoutErrorLine(ErrorKind.RateLimited));
    }

    [Fact]
    public void FlyoutErrorLine_NetworkErrorAndServerError_ReturnNull() // A8.11
    {
        Assert.Null(UsageText.FlyoutErrorLine(ErrorKind.NetworkError));
        Assert.Null(UsageText.FlyoutErrorLine(ErrorKind.ServerError));
    }

    [Fact]
    public void FlyoutErrorLine_NotLoggedIn_ReturnsFriendlyText() // A8.12
    {
        Assert.Equal("Not logged in to Claude Code", UsageText.FlyoutErrorLine(ErrorKind.NotLoggedIn));
    }

    [Fact]
    public void FlyoutErrorLine_CliRequiredForReauth_ReturnsFriendlyText() // A8.12
    {
        Assert.Equal("Claude Code CLI required for re-auth", UsageText.FlyoutErrorLine(ErrorKind.CliRequiredForReauth));
    }

    [Fact]
    public void FlyoutErrorLine_Null_ReturnsNull() // A8.13
    {
        Assert.Null(UsageText.FlyoutErrorLine(null));
    }

    // §8.3 — Updated-time timezone fix
    [Fact]
    public void Tooltip_UpdatedRendersInNowOffset_Eastern() // A8.14
    {
        var updated = DateTimeOffset.Parse("2026-06-30T13:44:00+00:00"); // UTC
        var now = DateTimeOffset.Parse("2026-06-30T09:44:00-04:00");     // Eastern
        var state = new LimitState(0, null, Present: true);
        Assert.Equal("5-hour · 0% · Updated 09:44",
            UsageText.Tooltip(LimitKind.Session5h, state, updated, now));
    }

    [Fact]
    public void Tooltip_UpdatedRendersInNowOffset_UTC() // A8.15
    {
        var updated = DateTimeOffset.Parse("2026-06-30T13:44:00+00:00");
        var now = DateTimeOffset.Parse("2026-06-30T13:44:00+00:00");
        var state = new LimitState(0, null, Present: true);
        Assert.Equal("5-hour · 0% · Updated 13:44",
            UsageText.Tooltip(LimitKind.Session5h, state, updated, now));
    }
}
