using CcUsageMonitor.Core.Services;
using SkiaSharp;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class RingRendererTests
{
    private const int DefaultWarn = 70;
    private const int DefaultAlert = 90;
    private const int DefaultSize = 32;

    // --- Rendering smoke tests: representative pcts + both showNumber states ---

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(-10)]
    public void Render_DoesNotThrow_RepresentativePcts(int pct)
    {
        var bitmap = RingRenderer.Render(pct, DefaultWarn, DefaultAlert, showNumber: true, DefaultSize);
        Assert.NotNull(bitmap);
        Assert.Equal(DefaultSize, bitmap.Width);
        Assert.Equal(DefaultSize, bitmap.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Render_DoesNotThrow_ShowNumberFalse(int pct)
    {
        var bitmap = RingRenderer.Render(pct, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        Assert.NotNull(bitmap);
        Assert.Equal(DefaultSize, bitmap.Width);
        Assert.Equal(DefaultSize, bitmap.Height);
    }

    // --- Null pct → dim track-only, no text ---

    [Fact]
    public void Render_NullPct_DrawsNoText()
    {
        RingRenderer.Render(null, DefaultWarn, DefaultAlert, showNumber: true, DefaultSize);
        Assert.Null(RingRenderer.LastDrawnText);
    }

    // --- Track is always drawn (even at 0%) ---

    [Fact]
    public void Render_0Pct_TrackStillDrawn_DimGray()
    {
        var bitmap = RingRenderer.Render(0, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // At 0% there's no arc, but the track circle should still be dim gray.
        // Top position (12 o'clock on the track stroke)
        var topTrack = bitmap.GetPixel(16, 1);
        // Track is dim gray #222222 (34,34,34). Allow tolerance for anti-aliasing.
        const int tolerance = 40;
        Assert.True(Math.Abs(topTrack.Red - 34) <= tolerance,
            $"Top track pixel should be dim gray ~34, got {topTrack.Red}");
        Assert.True(Math.Abs(topTrack.Green - 34) <= tolerance,
            $"Top track pixel should be dim gray ~34, got {topTrack.Green}");
        Assert.True(Math.Abs(topTrack.Blue - 34) <= tolerance,
            $"Top track pixel should be dim gray ~34, got {topTrack.Blue}");
        Assert.True(topTrack.Alpha > 128, "Top track pixel should be opaque");
    }

    // --- Arc starts at 12 o'clock (top) ---

    [Fact]
    public void Render_ArcStartsAtTop_12Oclock()
    {
        // 50% arc spans -90° to +90° (top-right half). Top pixel should be on the arc.
        var bitmap = RingRenderer.Render(50, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        var topArc = bitmap.GetPixel(16, 1);
        // Should be on the arc (not just track), so red/green/blue should be band colors
        // At 50% the band is green #4CAF50 (76,175,80)
        Assert.True(topArc.Red > 50 && topArc.Green > 100,
            $"Top pixel should be on green arc, got {topArc}");
    }

    // --- Arc full at >=100 (clamped to 360° sweep) ---

    [Fact]
    public void Render_100Pct_FullArc()
    {
        var bitmap = RingRenderer.Render(100, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // 100% → red arc that covers the full circle.
        // Verify the right-side arc pixel is red (not just the dim track).
        var rightSide = bitmap.GetPixel(30, 16);
        Assert.True(rightSide.Red > 150 && rightSide.Green < 100,
            $"Right side should be red arc at 100%, got {rightSide}");
    }

    [Fact]
    public void Render_150Pct_ClampedToFullArc()
    {
        var bitmap = RingRenderer.Render(150, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // Should look the same as 100% (clamped arc)
        var rightSide = bitmap.GetPixel(30, 16);
        Assert.True(rightSide.Red > 150 && rightSide.Green < 100,
            $"150% should render full red arc (clamped), got {rightSide}");
    }

    // --- Drawn text seam: unclamped value ---

    [Fact]
    public void Render_DrawnTextEqualsUnclampedValue_103()
    {
        RingRenderer.Render(103, DefaultWarn, DefaultAlert, showNumber: true, DefaultSize);
        Assert.Equal("103", RingRenderer.LastDrawnText);
    }

    [Fact]
    public void Render_DrawnTextEqualsUnclampedValue_0()
    {
        RingRenderer.Render(0, DefaultWarn, DefaultAlert, showNumber: true, DefaultSize);
        Assert.Equal("0", RingRenderer.LastDrawnText);
    }

    [Fact]
    public void Render_DrawnTextEqualsUnclampedValue_72()
    {
        RingRenderer.Render(72, DefaultWarn, DefaultAlert, showNumber: true, DefaultSize);
        Assert.Equal("72", RingRenderer.LastDrawnText);
    }

    // --- Drawn text seam: no text when showNumber=false ---

    [Fact]
    public void Render_ShowNumberFalse_DrawsNoText()
    {
        RingRenderer.Render(95, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        Assert.Null(RingRenderer.LastDrawnText);
    }

    [Fact]
    public void Render_NullPct_ShowNumberFalse_DrawsNoText()
    {
        RingRenderer.Render(null, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        Assert.Null(RingRenderer.LastDrawnText);
    }

    // --- Color band selection via Bands (clamped pct) ---

    [Theory]
    [InlineData(69, BandType.Green)]
    [InlineData(70, BandType.Yellow)]
    [InlineData(89, BandType.Yellow)]
    [InlineData(90, BandType.Red)]
    [InlineData(99, BandType.Red)]
    [InlineData(100, BandType.Red)]
    public void Render_BandSelection_OnClampedPct(int pct, BandType expected)
    {
        var bitmap = RingRenderer.Render(pct, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);

        // Sample the top pixel of the arc (above center, at the 12-o'clock edge)
        var arcPixel = bitmap.GetPixel(16, 1);
        Color bandColor = expected switch
        {
            BandType.Green => new Color("#4CAF50"),
            BandType.Yellow => new Color("#FFC107"),
            BandType.Red => new Color("#F44336"),
            _ => throw new ArgumentOutOfRangeException(nameof(expected)),
        };

        // The arc pixel should match the expected band color (within small tolerance for rendering)
        Assert.True(AreSimilar(actual: arcPixel, expected: bandColor),
            $"At pct={pct} expected {expected} ({bandColor}) but got {arcPixel}");
    }

    // --- Custom thresholds prove they aren't hardcoded ---

    [Theory]
    [InlineData(49, BandType.Green)]
    [InlineData(50, BandType.Yellow)]
    [InlineData(79, BandType.Yellow)]
    [InlineData(80, BandType.Red)]
    public void Render_CustomThresholds(int pct, BandType expected)
    {
        var bitmap = RingRenderer.Render(pct, warnThreshold: 50, alertThreshold: 80, showNumber: false, DefaultSize);

        var arcPixel = bitmap.GetPixel(16, 1);
        Color bandColor = expected switch
        {
            BandType.Green => new Color("#4CAF50"),
            BandType.Yellow => new Color("#FFC107"),
            BandType.Red => new Color("#F44336"),
            _ => throw new ArgumentOutOfRangeException(nameof(expected)),
        };

        Assert.True(AreSimilar(actual: arcPixel, expected: bandColor),
            $"At pct={pct} with custom thresholds expected {expected} but got {arcPixel}");
    }

    // --- Default size is 32 ---

    [Fact]
    public void Render_DefaultSize_Is32()
    {
        var bitmap = RingRenderer.Render(50, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        Assert.Equal(32, bitmap.Width);
        Assert.Equal(32, bitmap.Height);
    }

    // --- Custom size ---

    [Fact]
    public void Render_CustomSize_64()
    {
        var bitmap = RingRenderer.Render(50, DefaultWarn, DefaultAlert, showNumber: false, 64);
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
    }

    // --- Negative pct clamped to 0 (arc drawn but no sweep) ---

    [Fact]
    public void Render_NegativePct_ClampedToZero_NoArc()
    {
        var bitmap = RingRenderer.Render(-10, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // -10 should be clamped to 0 → band is green, sweep is 0° (no arc drawn)
        // Top pixel should be track color (dim gray), not a band color
        var top = bitmap.GetPixel(16, 1);
        // Track is dim gray (#222222 ≈ 34,34,34). Not a bright band.
        Assert.True(top.Red < 60, $"Top pixel should be track color, got R={top.Red}");
        Assert.True(top.Green < 60, $"Top pixel should be track color, got G={top.Green}");
        Assert.True(top.Blue < 60, $"Top pixel should be track color, got B={top.Blue}");
    }

    // --- Helper: approximate color match (accounts for anti-aliasing pixels) ---

    private static bool AreSimilar(Color actual, Color expected)
    {
        const int tolerance = 40;
        return Math.Abs(actual.R - expected.R) <= tolerance &&
               Math.Abs(actual.G - expected.G) <= tolerance &&
               Math.Abs(actual.B - expected.B) <= tolerance &&
               actual.A > 128;
    }

    // --- Helper struct for color comparison ---

    private readonly struct Color
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public byte A { get; }

        public Color(int r, int g, int b, int a)
        {
            R = (byte)r;
            G = (byte)g;
            B = (byte)b;
            A = (byte)a;
        }

        public Color(string hex)
        {
            R = byte.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
            G = byte.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
            B = byte.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
            A = 255;
        }

        public static implicit operator Color(SKColor c) => new((int)c.Red, (int)c.Green, (int)c.Blue, (int)c.Alpha);
    }
}
