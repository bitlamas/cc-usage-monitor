using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class UsageParserTests
{
    private string GetFixture(string name) =>
        File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", name));

    #region Full payload parsing

    [Fact]
    public void UsageParser_Parse_FullPayload_AllKindsPresent()
    {
        // Arrange
        var json = GetFixture("usage_full.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.Null(snapshot.Error);
        Assert.False(snapshot.Stale);
        Assert.Equal(3, snapshot.Limits.Count);

        Assert.True(snapshot.Limits[LimitKind.Session5h].Present);
        Assert.Equal(72, snapshot.Limits[LimitKind.Session5h].Pct);
        Assert.NotNull(snapshot.Limits[LimitKind.Session5h].ResetsAt);

        Assert.True(snapshot.Limits[LimitKind.WeeklyAll].Present);
        Assert.Equal(46, snapshot.Limits[LimitKind.WeeklyAll].Pct); // 45.5 → 46 (AwayFromZero)

        Assert.True(snapshot.Limits[LimitKind.WeeklyFable].Present);
        Assert.Equal(63, snapshot.Limits[LimitKind.WeeklyFable].Pct);
        Assert.NotNull(snapshot.Limits[LimitKind.WeeklyFable].ResetsAt);
    }

    #endregion

    #region Fable (limits[] array) parsing

    [Fact]
    public void UsageParser_Parse_Fable_ReadsResetsAtFromScopedEntry()
    {
        // Arrange
        var json = GetFixture("usage_full.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert — Fable's reset is 2026-06-21T10:00:00Z from the weekly_scoped entry
        var resets = snapshot.Limits[LimitKind.WeeklyFable].ResetsAt;
        Assert.NotNull(resets);
        Assert.Equal(new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero), resets!.Value);
    }

    [Fact]
    public void UsageParser_Parse_Fable_PercentRoundsHalfUp()
    {
        // Arrange — usage_rounding.json Fable percent is 62.5
        var json = GetFixture("usage_rounding.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert — 62.5 → 63 (AwayFromZero)
        Assert.True(snapshot.Limits[LimitKind.WeeklyFable].Present);
        Assert.Equal(63, snapshot.Limits[LimitKind.WeeklyFable].Pct);
    }

    [Fact]
    public void UsageParser_Parse_NoLimitsArray_FableAbsent()
    {
        // Arrange — usage_null_utilization.json has no limits[] array at all
        var json = GetFixture("usage_null_utilization.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.False(snapshot.Limits[LimitKind.WeeklyFable].Present);
        Assert.Null(snapshot.Limits[LimitKind.WeeklyFable].Pct);
        Assert.Null(snapshot.Limits[LimitKind.WeeklyFable].ResetsAt);
    }

    [Fact]
    public void UsageParser_Parse_LimitsArrayWithoutFable_FableAbsent()
    {
        // Arrange — limits[] present but the only weekly_scoped entry is model "Sonnet"
        var json = GetFixture("usage_no_fable.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert — a non-Fable scoped model must NOT be mistaken for Fable
        Assert.False(snapshot.Limits[LimitKind.WeeklyFable].Present);
        Assert.Null(snapshot.Limits[LimitKind.WeeklyFable].Pct);
    }

    [Fact]
    public void UsageParser_Parse_FableMatchIsCaseSensitive()
    {
        // Arrange — "fable" (lowercase) must not match the exact API display name "Fable"
        var json = @"{
            ""five_hour"": {""utilization"": 10.0},
            ""limits"": [
                {""kind"": ""weekly_scoped"", ""percent"": 63,
                 ""scope"": {""model"": {""display_name"": ""fable""}}}
            ]
        }";

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.False(snapshot.Limits[LimitKind.WeeklyFable].Present);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void UsageParser_Parse_NullUtilization_PctNull()
    {
        // Arrange
        var json = GetFixture("usage_null_utilization.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        // seven_day: utilization is null → Pct = null, Present = true
        Assert.True(snapshot.Limits[LimitKind.WeeklyAll].Present);
        Assert.Null(snapshot.Limits[LimitKind.WeeklyAll].Pct);

        // five_hour: no utilization key at all → Pct = null
        Assert.True(snapshot.Limits[LimitKind.Session5h].Present);
        Assert.Null(snapshot.Limits[LimitKind.Session5h].Pct);
    }

    [Fact]
    public void UsageParser_Parse_RoundHalfUp_AwayFromZero()
    {
        // Arrange
        var json = GetFixture("usage_rounding.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert — 72.5 → 73 (AwayFromZero, not banker's 72)
        Assert.Equal(73, snapshot.Limits[LimitKind.Session5h].Pct);

        // 125.0 → 125 (unclamped, per spec)
        Assert.Equal(125, snapshot.Limits[LimitKind.WeeklyAll].Pct);
    }

    [Fact]
    public void UsageParser_Parse_ResetsAt_FractionalSeconds()
    {
        // Arrange
        var json = GetFixture("usage_full.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert — resets_at has fractional seconds and +00:00 offset
        var resets = snapshot.Limits[LimitKind.Session5h].ResetsAt;
        Assert.NotNull(resets);
        Assert.Equal(2026, resets.Value.Year);
        Assert.Equal(6, resets.Value.Month);
        Assert.Equal(18, resets.Value.Day);
        Assert.Equal(18, resets.Value.Hour);
        Assert.Equal(49, resets.Value.Minute);
        Assert.Equal(59, resets.Value.Second);
        Assert.Equal(TimeSpan.Zero, resets.Value.Offset); // +00:00
    }

    [Fact]
    public void UsageParser_Parse_IgnoresUnknownBuckets()
    {
        // Arrange
        var json = GetFixture("usage_full.json"); // includes seven_day_cowork, tangelo, dead sonnet/opus

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert — only the 3 target kinds should be in Limits
        Assert.Equal(3, snapshot.Limits.Count);
        Assert.Contains(LimitKind.Session5h, snapshot.Limits.Keys);
        Assert.Contains(LimitKind.WeeklyAll, snapshot.Limits.Keys);
        Assert.Contains(LimitKind.WeeklyFable, snapshot.Limits.Keys);
    }

    [Fact]
    public void UsageParser_Parse_EmptyBody_AllAbsent()
    {
        // Arrange
        var json = GetFixture("usage_empty.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.Equal(3, snapshot.Limits.Count);
        Assert.All(snapshot.Limits.Values, s => Assert.False(s.Present));
    }

    [Fact]
    public void UsageParser_Parse_MalformedJson_ThrowsJsonException()
    {
        // Arrange
        var json = GetFixture("usage_malformed.json");

        // Act + Assert
        // Malformed JSON throws a JsonReaderException (subclass of JsonException) from
        // System.Text.Json. Use ThrowsAny for framework-version-proof type matching.
        var ex = Assert.ThrowsAny<JsonException>(() => UsageParser.Parse(json));
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void UsageParser_Parse_NumericUtilization_NoMultiply100()
    {
        // Arrange — utilization is 0–100, not 0–10000
        var json = @"{""five_hour"": {""utilization"": 72.0, ""resets_at"": ""2026-06-18T18:00:00+00:00""}}";

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert — 72.0 → 72, not 7200
        Assert.Equal(72, snapshot.Limits[LimitKind.Session5h].Pct);
    }

    [Fact]
    public void UsageParser_Parse_ResetsAtWithPlusOffset_ParsesCorrectly()
    {
        // Arrange
        var json = @"{
            ""five_hour"": {""utilization"": 50.0, ""resets_at"": ""2026-06-18T18:49:59.201483+00:00""},
            ""seven_day"": {""utilization"": 30.0, ""resets_at"": ""2026-06-22T00:00:00.000000+00:00""}
        }";

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.NotNull(snapshot.Limits[LimitKind.Session5h].ResetsAt);
        Assert.Equal(TimeSpan.Zero, snapshot.Limits[LimitKind.Session5h].ResetsAt!.Value.Offset);
        // Fable is absent when no limits[] array is present
        Assert.False(snapshot.Limits[LimitKind.WeeklyFable].Present);
    }

    #endregion
}
