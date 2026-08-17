using System;
using System.Collections.Generic;
using System.Globalization;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class LogFormatTests
{
    #region Line tests

    [Fact]
    public void Line_Error_ExactFormat() // A4.1
    {
        var ts = DateTimeOffset.Parse("2026-07-14T18:54:03.123-04:00");
        Assert.Equal("2026-07-14 18:54:03.123 -04:00 [ERROR] boom", LogFormat.Line(ts, LogLevel.Error, "boom"));
    }

    [Fact]
    public void Line_Info_ExactFormat() // A4.2
    {
        var ts = DateTimeOffset.Parse("2026-07-14T18:54:03.000+00:00");
        Assert.Equal("2026-07-14 18:54:03.000 +00:00 [INFO] up", LogFormat.Line(ts, LogLevel.Info, "up"));
    }

    [Theory] // A4.3
    [InlineData(LogLevel.Info, "INFO")]
    [InlineData(LogLevel.Warn, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Fatal, "FATAL")]
    public void Line_LevelToken(LogLevel lvl, string token)
    {
        var ts = DateTimeOffset.Parse("2026-07-14T18:54:03.000+00:00");
        Assert.Equal($"2026-07-14 18:54:03.000 +00:00 [{token}] x", LogFormat.Line(ts, lvl, "x"));
    }

    [Fact] // A4.9c — invariant culture
    public void Line_InvariantUnderCulture()
    {
        // fi-FI's time separator is "." (not ":"), so under this culture a format string built
        // WITHOUT CultureInfo.InvariantCulture emits "18.54.03" — this test actually catches a
        // dropped invariant pin. (de-DE, the original choice, uses ":" like invariant and so
        // passes either way, making it an inert guard.)
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            var ts = DateTimeOffset.Parse("2026-07-14T18:54:03.123-04:00");
            Assert.Equal("2026-07-14 18:54:03.123 -04:00 [ERROR] boom", LogFormat.Line(ts, LogLevel.Error, "boom"));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    #endregion

    #region Poll tests

    private static UsageSnapshot Snap(
        Dictionary<LimitKind, LimitState> limits,
        ErrorKind? kind = null,
        int fails = 0)
    {
        return new(
            limits,
            DateTimeOffset.Parse("2026-07-14T00:00:00Z"),
            kind is null ? null : "e",
            kind is not null,
            kind,
            null,
            fails);
    }

    [Fact] // A4.4
    public void Poll_Success_FixedOrder()
    {
        var d = new Dictionary<LimitKind, LimitState>
        {
            [LimitKind.Session5h] = new(42, null, true),
            [LimitKind.WeeklyAll] = new(88, null, true),
            [LimitKind.WeeklyFable] = new(null, null, false),
        };
        Assert.Equal("poll ok · 5h=42% · weekly=88% · fable=--%", LogFormat.Poll(Snap(d)));
    }

    [Fact] // A4.5
    public void Poll_Success_AllPresent_Unclamped()
    {
        var d = new Dictionary<LimitKind, LimitState>
        {
            [LimitKind.Session5h] = new(0, null, true),
            [LimitKind.WeeklyAll] = new(100, null, true),
            [LimitKind.WeeklyFable] = new(150, null, true),
        };
        Assert.Equal("poll ok · 5h=0% · weekly=100% · fable=150%", LogFormat.Poll(Snap(d)));
    }

    [Fact] // A4.6
    public void Poll_Failure_WithKindAndFails()
    {
        var d = new Dictionary<LimitKind, LimitState>
        {
            [LimitKind.Session5h] = new(null, null, false),
            [LimitKind.WeeklyAll] = new(null, null, false),
            [LimitKind.WeeklyFable] = new(null, null, false),
        };
        Assert.Equal(
            "poll err · kind=CliRequiredForReauth · fails=2 · 5h=--% · weekly=--% · fable=--%",
            LogFormat.Poll(Snap(d, ErrorKind.CliRequiredForReauth, 2)));
    }

    [Fact] // A4.7
    public void Poll_Failure_MergedValues()
    {
        var d = new Dictionary<LimitKind, LimitState>
        {
            [LimitKind.Session5h] = new(50, null, true),
            [LimitKind.WeeklyAll] = new(88, null, true),
            [LimitKind.WeeklyFable] = new(null, null, false),
        };
        Assert.Equal(
            "poll err · kind=RateLimited · fails=1 · 5h=50% · weekly=88% · fable=--%",
            LogFormat.Poll(Snap(d, ErrorKind.RateLimited, 1)));
    }

    [Fact] // A4.8 — glyph check
    public void Poll_SeparatorIsMiddleDot()
    {
        var d = new Dictionary<LimitKind, LimitState>
        {
            [LimitKind.Session5h] = new(42, null, true),
            [LimitKind.WeeklyAll] = new(88, null, true),
            [LimitKind.WeeklyFable] = new(null, null, false),
        };
        var result = LogFormat.Poll(Snap(d));
        Assert.Equal("poll ok · 5h=42% · weekly=88% · fable=--%", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain(" . ", result);
    }

    [Fact] // A4.9 — reverse insertion order still canonical
    public void Poll_ReverseInsertion_StillCanonical()
    {
        var d = new Dictionary<LimitKind, LimitState>();
        d[LimitKind.WeeklyFable] = new(null, null, false);
        d[LimitKind.WeeklyAll] = new(88, null, true);
        d[LimitKind.Session5h] = new(42, null, true);
        Assert.Equal("poll ok · 5h=42% · weekly=88% · fable=--%", LogFormat.Poll(Snap(d)));
    }

    [Fact] // A4.9b — key omitted entirely
    public void Poll_FableKeyOmitted_RendersDashes()
    {
        var d = new Dictionary<LimitKind, LimitState>
        {
            [LimitKind.Session5h] = new(42, null, true),
            [LimitKind.WeeklyAll] = new(88, null, true),
        };
        Assert.Equal("poll ok · 5h=42% · weekly=88% · fable=--%", LogFormat.Poll(Snap(d)));
    }

    #endregion
}
