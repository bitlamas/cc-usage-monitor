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

    // --- Null pct → dim disc only, no text ---

    [Fact]
    public void Render_NullPct_DrawsNoText()
    {
        RingRenderer.Render(null, DefaultWarn, DefaultAlert, showNumber: true, DefaultSize);
        Assert.Null(RingRenderer.LastDrawnText);
    }

    [Fact]
    public void Render_NullPct_DrawsTransparentDiscWithOutline()
    {
        var bitmap = RingRenderer.Render(null, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // Center pixel should be transparent (no wedge, transparent track).
        var center = bitmap.GetPixel(16, 16);
        Assert.True(center.Alpha < 10,
            $"Center should be transparent, got alpha={center.Alpha}");
        // Outline: top edge pixel should be dark (the 1px stroke ring).
        var topEdge = bitmap.GetPixel(16, 1);
        Assert.True(topEdge.Red < 60 && topEdge.Green < 60 && topEdge.Blue < 60,
            $"Top edge should be dark outline, got {topEdge}");
    }

    // --- Filled disc track is drawn (even at 0%) ---

    [Fact]
    public void Render_0Pct_TrackIsTransparentWithOutline()
    {
        var bitmap = RingRenderer.Render(0, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // At 0% there's no wedge, transparent fill with thin outline.
        var center = bitmap.GetPixel(16, 16);
        Assert.True(center.Alpha < 10,
            $"Center should be transparent, got alpha={center.Alpha}");
        // Outline at edge.
        var topEdge = bitmap.GetPixel(16, 1);
        Assert.True(topEdge.Red < 60 && topEdge.Green < 60 && topEdge.Blue < 60,
            $"Top edge should be dark outline, got {topEdge}");
    }

    // --- Wedge starts at 12 o'clock (top), filled disc ---

    [Fact]
    public void Render_WedgeStartsAtTop_12Oclock()
    {
        // 50% wedge spans -90° to +90° (top-right half). A pixel just off-center
        // along the 12-o'clock radius (inside the wedge) should be band-colored.
        var bitmap = RingRenderer.Render(50, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // Sample a pixel inside the wedge: (16, 12) — on the 12-o'clock radius, just off-center.
        var topWedge = bitmap.GetPixel(16, 12);
        // At 50% the band is green #4CAF50 (76,175,80)
        Assert.True(topWedge.Red > 50 && topWedge.Green > 100,
            $"Top wedge pixel should be green, got {topWedge}");
    }

    // --- Filled disc full at >=100 (clamped to 360° sweep) ---

    [Fact]
    public void Render_100Pct_FullDisc()
    {
        var bitmap = RingRenderer.Render(100, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // 100% → red disc that covers the entire radius.
        // Right-side pixel should be red (not just the dim track).
        var rightSide = bitmap.GetPixel(30, 16);
        Assert.True(rightSide.Red > 150 && rightSide.Green < 100,
            $"Right side should be red at 100%, got {rightSide}");
    }

    [Fact]
    public void Render_150Pct_ClampedToFullDisc()
    {
        var bitmap = RingRenderer.Render(150, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // Should look the same as 100% (clamped wedge)
        var rightSide = bitmap.GetPixel(30, 16);
        Assert.True(rightSide.Red > 150 && rightSide.Green < 100,
            $"150% should render full red disc (clamped), got {rightSide}");
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

    // --- Color band selection via Bands (clamped pct) — sample inside wedge ---

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

        // Sample a pixel inside the wedge: (16, 12) — on the 12-o'clock radius, inside the disc.
        // For pct >= ~25 this pixel falls inside the wedge; at 100% it always does.
        var wedgePixel = bitmap.GetPixel(16, 12);
        Color bandColor = expected switch
        {
            BandType.Green => new Color("#4CAF50"),
            BandType.Yellow => new Color("#FFC107"),
            BandType.Red => new Color("#F44336"),
            _ => throw new ArgumentOutOfRangeException(nameof(expected)),
        };

        Assert.True(AreSimilar(actual: wedgePixel, expected: bandColor),
            $"At pct={pct} expected {expected} ({bandColor}) but got {wedgePixel}");
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

        var wedgePixel = bitmap.GetPixel(16, 12);
        Color bandColor = expected switch
        {
            BandType.Green => new Color("#4CAF50"),
            BandType.Yellow => new Color("#FFC107"),
            BandType.Red => new Color("#F44336"),
            _ => throw new ArgumentOutOfRangeException(nameof(expected)),
        };

        Assert.True(AreSimilar(actual: wedgePixel, expected: bandColor),
            $"At pct={pct} with custom thresholds expected {expected} but got {wedgePixel}");
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

    // --- Negative pct clamped to 0 (wedge drawn but no sweep) ---

    [Fact]
    public void Render_NegativePct_ClampedToZero_NoWedge()
    {
        var bitmap = RingRenderer.Render(-10, DefaultWarn, DefaultAlert, showNumber: false, DefaultSize);
        // -10 clamped to 0 → band is green, sweep 0° (no wedge).
        // Center transparent, outline at edge.
        var center = bitmap.GetPixel(16, 16);
        Assert.True(center.Alpha < 10, $"Center should be transparent, got alpha={center.Alpha}");
        var topEdge = bitmap.GetPixel(16, 1);
        Assert.True(topEdge.Red < 60 && topEdge.Green < 60 && topEdge.Blue < 60,
            $"Top edge should be dark outline, got {topEdge}");
    }

    // --- Text outline seam ---

    [Fact]
    public void Render_TextOutlineThickness_IsPositive()
    {
        // Prove the outline seam constant exists and is positive (a real outline is drawn).
        Assert.True(RingRenderer.TextOutlineThickness > 0,
            $"TextOutlineThickness should be positive, got {RingRenderer.TextOutlineThickness}");
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
