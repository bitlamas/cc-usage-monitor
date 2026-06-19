using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class BandsTests
{
    // Default thresholds
    private const int DefaultWarn = 70;
    private const int DefaultAlert = 90;

    [Theory]
    [InlineData(0, BandType.Green)]
    [InlineData(49, BandType.Green)]
    [InlineData(50, BandType.Green)]
    [InlineData(69, BandType.Green)]
    [InlineData(70, BandType.Yellow)]
    [InlineData(89, BandType.Yellow)]
    [InlineData(90, BandType.Red)]
    [InlineData(99, BandType.Red)]
    [InlineData(100, BandType.Red)]
    public void Bands_Select_DefaultThresholds(int pct, BandType expected)
    {
        Assert.Equal(expected, Bands.Select(pct, DefaultWarn, DefaultAlert));
    }

    [Fact]
    public void Bands_Select_NegativePct_UsesDim()
    {
        // Negative pct after clamping to 0 — should use band logic on 0
        Assert.Equal(BandType.Green, Bands.Select(-10, DefaultWarn, DefaultAlert));
    }

    [Fact]
    public void Bands_Select_ClampsTo100()
    {
        // pct=125 should be clamped to 100 → Red
        Assert.Equal(BandType.Red, Bands.Select(125, DefaultWarn, DefaultAlert));
    }

    [Fact]
    public void Bands_Select_NullPct_ReturnsDim()
    {
        Assert.Equal(BandType.Dim, Bands.Select(null, DefaultWarn, DefaultAlert));
    }

    // Custom thresholds prove they aren't hardcoded
    [Theory]
    [InlineData(49, BandType.Green)]
    [InlineData(50, BandType.Yellow)]
    [InlineData(79, BandType.Yellow)]
    [InlineData(80, BandType.Red)]
    [InlineData(100, BandType.Red)]
    public void Bands_Select_CustomThresholds(int pct, BandType expected)
    {
        const int warn = 50;
        const int alert = 80;
        Assert.Equal(expected, Bands.Select(pct, warn, alert));
    }

    [Fact]
    public void Bands_Select_NullPct_CustomThresholds_ReturnsDim()
    {
        Assert.Equal(BandType.Dim, Bands.Select(null, 50, 80));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(-50)]
    public void Bands_Select_EdgePct_ReturnsValidBand(int pct)
    {
        var band = Bands.Select(pct, DefaultWarn, DefaultAlert);
        Assert.True(Enum.IsDefined(typeof(BandType), band));
    }
}
