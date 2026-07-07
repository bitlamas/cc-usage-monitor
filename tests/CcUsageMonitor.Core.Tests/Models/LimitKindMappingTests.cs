using CcUsageMonitor.Core.Models;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Models;

public class LimitKindMappingTests
{
    // Bucket keys per spec §5 (seven_day_fable is a synthetic key — Fable is sourced
    // from the API limits[] array, not a top-level bucket).
    private const string BucketSession5h = "five_hour";
    private const string BucketWeeklyAll = "seven_day";
    private const string BucketWeeklyFable = "seven_day_fable";

    // Display labels per spec §5
    private const string LabelSession5h = "5-hour session";
    private const string LabelWeeklyAll = "Weekly (all models)";
    private const string LabelWeeklyFable = "Weekly (Fable)";

    [Fact]
    public void LimitKind_Session5h_MapsToCorrectBucketKey()
    {
        // Assert
        Assert.Equal(BucketSession5h, LimitKindMapping.GetBucketKey(LimitKind.Session5h));
    }

    [Fact]
    public void LimitKind_WeeklyAll_MapsToCorrectBucketKey()
    {
        Assert.Equal(BucketWeeklyAll, LimitKindMapping.GetBucketKey(LimitKind.WeeklyAll));
    }

    [Fact]
    public void LimitKind_WeeklyFable_MapsToCorrectBucketKey()
    {
        Assert.Equal(BucketWeeklyFable, LimitKindMapping.GetBucketKey(LimitKind.WeeklyFable));
    }

    [Theory]
    [InlineData(LimitKind.Session5h, LabelSession5h)]
    [InlineData(LimitKind.WeeklyAll, LabelWeeklyAll)]
    [InlineData(LimitKind.WeeklyFable, LabelWeeklyFable)]
    public void LimitKind_MapsToCorrectLabel(LimitKind kind, string expectedLabel)
    {
        Assert.Equal(expectedLabel, LimitKindMapping.GetLabel(kind));
    }

    [Theory]
    [InlineData(BucketSession5h, LimitKind.Session5h)]
    [InlineData(BucketWeeklyAll, LimitKind.WeeklyAll)]
    [InlineData(BucketWeeklyFable, LimitKind.WeeklyFable)]
    public void LimitKind_RoundTripBucketKey(string bucketKey, LimitKind expectedKind)
    {
        Assert.Equal(expectedKind, LimitKindMapping.GetKindFromBucketKey(bucketKey));
    }

    [Theory]
    [InlineData(LabelSession5h, LimitKind.Session5h)]
    [InlineData(LabelWeeklyAll, LimitKind.WeeklyAll)]
    [InlineData(LabelWeeklyFable, LimitKind.WeeklyFable)]
    public void LimitKind_RoundTripLabel(string label, LimitKind expectedKind)
    {
        Assert.Equal(expectedKind, LimitKindMapping.GetKindFromLabel(label));
    }

    [Fact]
    public void LimitKind_UnknownBucketKey_ReturnsNull()
    {
        Assert.Null(LimitKindMapping.GetKindFromBucketKey("nonexistent_bucket"));
    }

    [Fact]
    public void LimitKind_RemovedSonnetBucketKey_ReturnsNull()
    {
        // Sonnet/Opus buckets were removed in v1.2.0 — they must no longer resolve.
        Assert.Null(LimitKindMapping.GetKindFromBucketKey("seven_day_sonnet"));
        Assert.Null(LimitKindMapping.GetKindFromBucketKey("seven_day_opus"));
    }

    [Fact]
    public void LimitKind_UnknownLabel_ReturnsNull()
    {
        Assert.Null(LimitKindMapping.GetKindFromLabel("Unknown Label"));
    }

    [Fact]
    public void LimitKind_AllKinds_IsExactlyThree()
    {
        Assert.Equal(
            new[] { LimitKind.Session5h, LimitKind.WeeklyAll, LimitKind.WeeklyFable },
            LimitKindMapping.AllKinds);
    }

    [Fact]
    public void LimitKind_AllBucketKeysAreUnique()
    {
        var keys = LimitKindMapping.AllKinds.Select(k => LimitKindMapping.GetBucketKey(k)).ToList();
        Assert.Equal(keys.Distinct(), keys);
    }

    [Fact]
    public void LimitKind_AllLabelsAreUnique()
    {
        var labels = LimitKindMapping.AllKinds.Select(k => LimitKindMapping.GetLabel(k)).ToList();
        Assert.Equal(labels.Distinct(), labels);
    }
}
