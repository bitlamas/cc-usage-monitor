using CcUsageMonitor.Core.Models;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Models;

public class LimitKindMappingTests
{
    // Bucket keys per spec §5
    private const string BucketSession5h = "five_hour";
    private const string BucketWeeklyAll = "seven_day";
    private const string BucketWeeklySonnet = "seven_day_sonnet";
    private const string BucketWeeklyOpus = "seven_day_opus";

    // Display labels per spec §5
    private const string LabelSession5h = "5-hour session";
    private const string LabelWeeklyAll = "Weekly (all models)";
    private const string LabelWeeklySonnet = "Weekly (Sonnet)";
    private const string LabelWeeklyOpus = "Weekly (Opus)";

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
    public void LimitKind_WeeklySonnet_MapsToCorrectBucketKey()
    {
        Assert.Equal(BucketWeeklySonnet, LimitKindMapping.GetBucketKey(LimitKind.WeeklySonnet));
    }

    [Fact]
    public void LimitKind_WeeklyOpus_MapsToCorrectBucketKey()
    {
        Assert.Equal(BucketWeeklyOpus, LimitKindMapping.GetBucketKey(LimitKind.WeeklyOpus));
    }

    [Theory]
    [InlineData(LimitKind.Session5h, LabelSession5h)]
    [InlineData(LimitKind.WeeklyAll, LabelWeeklyAll)]
    [InlineData(LimitKind.WeeklySonnet, LabelWeeklySonnet)]
    [InlineData(LimitKind.WeeklyOpus, LabelWeeklyOpus)]
    public void LimitKind_MapsToCorrectLabel(LimitKind kind, string expectedLabel)
    {
        Assert.Equal(expectedLabel, LimitKindMapping.GetLabel(kind));
    }

    [Theory]
    [InlineData(BucketSession5h, LimitKind.Session5h)]
    [InlineData(BucketWeeklyAll, LimitKind.WeeklyAll)]
    [InlineData(BucketWeeklySonnet, LimitKind.WeeklySonnet)]
    [InlineData(BucketWeeklyOpus, LimitKind.WeeklyOpus)]
    public void LimitKind_RoundTripBucketKey(string bucketKey, LimitKind expectedKind)
    {
        Assert.Equal(expectedKind, LimitKindMapping.GetKindFromBucketKey(bucketKey));
    }

    [Theory]
    [InlineData(LabelSession5h, LimitKind.Session5h)]
    [InlineData(LabelWeeklyAll, LimitKind.WeeklyAll)]
    [InlineData(LabelWeeklySonnet, LimitKind.WeeklySonnet)]
    [InlineData(LabelWeeklyOpus, LimitKind.WeeklyOpus)]
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
    public void LimitKind_UnknownLabel_ReturnsNull()
    {
        Assert.Null(LimitKindMapping.GetKindFromLabel("Unknown Label"));
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
