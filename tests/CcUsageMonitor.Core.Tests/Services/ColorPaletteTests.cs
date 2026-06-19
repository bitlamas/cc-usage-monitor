using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class ColorPaletteTests
{
    [Theory]
    [InlineData(BandType.Green, "#4CAF50")]
    [InlineData(BandType.Yellow, "#FFC107")]
    [InlineData(BandType.Red, "#F44336")]
    [InlineData(BandType.Dim, "#757575")]
    public void ColorPalette_Default_ReturnsCorrectColor(BandType band, string expected)
    {
        var palette = new ColorPalette();
        Assert.Equal(expected, palette.GetColor(band));
    }

    [Fact]
    public void ColorPalette_OverrideGreen_UsesOverride()
    {
        var palette = new ColorPalette(new Dictionary<string, string> { { "green", "#00FF00" } });
        Assert.Equal("#00FF00", palette.GetColor(BandType.Green));
    }

    [Fact]
    public void ColorPalette_OverrideYellow_UsesOverride()
    {
        var palette = new ColorPalette(new Dictionary<string, string> { { "yellow", "#FFFF00" } });
        Assert.Equal("#FFFF00", palette.GetColor(BandType.Yellow));
    }

    [Fact]
    public void ColorPalette_OverrideRed_UsesOverride()
    {
        var palette = new ColorPalette(new Dictionary<string, string> { { "red", "#FF0000" } });
        Assert.Equal("#FF0000", palette.GetColor(BandType.Red));
    }

    [Fact]
    public void ColorPalette_OverrideDim_UsesOverride()
    {
        var palette = new ColorPalette(new Dictionary<string, string> { { "dim", "#999999" } });
        Assert.Equal("#999999", palette.GetColor(BandType.Dim));
    }

    [Fact]
    public void ColorPalette_OverrideTrack_UsesOverride()
    {
        var palette = new ColorPalette(new Dictionary<string, string> { { "track", "#333333" } });
        Assert.Equal("#333333", palette.GetColor(BandType.Track));
    }

    [Fact]
    public void ColorPalette_MultipleOverrides_AllApplied()
    {
        var overrides = new Dictionary<string, string>
        {
            { "green", "#00FF00" },
            { "red", "#FF0000" },
            { "dim", "#CCCCCC" }
        };
        var palette = new ColorPalette(overrides);
        Assert.Equal("#00FF00", palette.GetColor(BandType.Green));
        Assert.Equal("#FFC107", palette.GetColor(BandType.Yellow)); // default
        Assert.Equal("#FF0000", palette.GetColor(BandType.Red));
        Assert.Equal("#CCCCCC", palette.GetColor(BandType.Dim));
        Assert.Equal("#222222", palette.GetColor(BandType.Track)); // default
    }

    [Fact]
    public void ColorPalette_EmptyOverrides_UsesAllDefaults()
    {
        var palette = new ColorPalette(new Dictionary<string, string>());
        Assert.Equal("#4CAF50", palette.GetColor(BandType.Green));
        Assert.Equal("#FFC107", palette.GetColor(BandType.Yellow));
        Assert.Equal("#F44336", palette.GetColor(BandType.Red));
        Assert.Equal("#757575", palette.GetColor(BandType.Dim));
        Assert.Equal("#222222", palette.GetColor(BandType.Track));
    }

    [Fact]
    public void ColorPalette_NullOverrides_UsesAllDefaults()
    {
        var palette = new ColorPalette((IReadOnlyDictionary<string, string>?)null);
        Assert.Equal("#4CAF50", palette.GetColor(BandType.Green));
    }

    [Fact]
    public void ColorPalette_AllBandTypes_Returned()
    {
        var palette = new ColorPalette();
        var green = palette.GetColor(BandType.Green);
        var yellow = palette.GetColor(BandType.Yellow);
        var red = palette.GetColor(BandType.Red);
        var dim = palette.GetColor(BandType.Dim);
        var track = palette.GetColor(BandType.Track);

        Assert.NotEmpty(green);
        Assert.NotEmpty(yellow);
        Assert.NotEmpty(red);
        Assert.NotEmpty(dim);
        Assert.NotEmpty(track);
    }
}
