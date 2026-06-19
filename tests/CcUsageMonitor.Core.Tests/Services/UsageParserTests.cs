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
        Assert.Equal(4, snapshot.Limits.Count);

        Assert.True(snapshot.Limits[LimitKind.Session5h].Present);
        Assert.Equal(72, snapshot.Limits[LimitKind.Session5h].Pct);
        Assert.NotNull(snapshot.Limits[LimitKind.Session5h].ResetsAt);

        Assert.True(snapshot.Limits[LimitKind.WeeklyAll].Present);
        Assert.Equal(46, snapshot.Limits[LimitKind.WeeklyAll].Pct); // 45.5 → 46 (AwayFromZero)

        Assert.True(snapshot.Limits[LimitKind.WeeklySonnet].Present);
        Assert.Equal(60, snapshot.Limits[LimitKind.WeeklySonnet].Pct);
        Assert.Null(snapshot.Limits[LimitKind.WeeklySonnet].ResetsAt);

        Assert.True(snapshot.Limits[LimitKind.WeeklyOpus].Present);
        Assert.Equal(10, snapshot.Limits[LimitKind.WeeklyOpus].Pct);
    }

    [Fact]
    public void UsageParser_Parse_MissingOpusBucket_OpusAbsent()
    {
        // Arrange — seven_day_opus key is completely absent
        var json = GetFixture("usage_missing_opus.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.False(snapshot.Limits[LimitKind.WeeklyOpus].Present);
        Assert.Null(snapshot.Limits[LimitKind.WeeklyOpus].Pct);
        Assert.Null(snapshot.Limits[LimitKind.WeeklyOpus].ResetsAt);
    }

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

        // seven_day_sonnet: entire bucket is null JSON → Present = false
        Assert.False(snapshot.Limits[LimitKind.WeeklySonnet].Present);

        // seven_day_opus: normal
        Assert.True(snapshot.Limits[LimitKind.WeeklyOpus].Present);
        Assert.Equal(5, snapshot.Limits[LimitKind.WeeklyOpus].Pct);
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

        // 50.0 → 50
        Assert.Equal(50, snapshot.Limits[LimitKind.WeeklySonnet].Pct);
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
    public void UsageParser_Parse_NullSonnetResetsAt_KeptNull()
    {
        // Arrange
        var json = GetFixture("usage_full.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.Null(snapshot.Limits[LimitKind.WeeklySonnet].ResetsAt);
    }

    [Fact]
    public void UsageParser_Parse_IgnoresUnknownBuckets()
    {
        // Arrange
        var json = GetFixture("usage_full.json"); // includes seven_day_cowork, tangelo

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert — only the 4 target keys should be in Limits
        Assert.Equal(4, snapshot.Limits.Count);
        Assert.Contains(LimitKind.Session5h, snapshot.Limits.Keys);
        Assert.Contains(LimitKind.WeeklyAll, snapshot.Limits.Keys);
        Assert.Contains(LimitKind.WeeklySonnet, snapshot.Limits.Keys);
        Assert.Contains(LimitKind.WeeklyOpus, snapshot.Limits.Keys);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void UsageParser_Parse_EmptyBody_AllAbsent()
    {
        // Arrange
        var json = GetFixture("usage_empty.json");

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.Equal(4, snapshot.Limits.Count);
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
            ""seven_day"": {""utilization"": 30.0, ""resets_at"": ""2026-06-22T00:00:00.000000+00:00""},
            ""seven_day_sonnet"": {""utilization"": 20.0, ""resets_at"": null},
            ""seven_day_opus"": {""utilization"": 10.0, ""resets_at"": ""2026-06-20T00:00:00+00:00""}
        }";

        // Act
        var snapshot = UsageParser.Parse(json);

        // Assert
        Assert.NotNull(snapshot.Limits[LimitKind.Session5h].ResetsAt);
        Assert.Equal(TimeSpan.Zero, snapshot.Limits[LimitKind.Session5h].ResetsAt!.Value.Offset);
        Assert.Null(snapshot.Limits[LimitKind.WeeklySonnet].ResetsAt);
    }

    #endregion
}
