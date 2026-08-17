using System.Globalization;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor.Core.Tests.Services;

/// <summary>
/// The key is the guard that stops the tray-icon handle leak, so these tests pin the
/// exact emitted string rather than asserting "contains" — a key that merely looks
/// plausible can still collide across distinct states and suppress a real redraw.
/// </summary>
public class TrayIconKeyTests
{
    private static LimitState State(int? pct) => new(pct, null, true);

    [Fact]
    public void For_ExactFormat_WithPercent()
    {
        Assert.Equal("42|-|70|90", TrayIconKey.For(State(42), showNumber: false, 70, 90));
    }

    [Fact]
    public void For_ExactFormat_ShowNumberOn()
    {
        Assert.Equal("42|n|70|90", TrayIconKey.For(State(42), showNumber: true, 70, 90));
    }

    [Fact]
    public void For_NullPercent_EmitsDash_NotEmpty()
    {
        Assert.Equal("-|-|70|90", TrayIconKey.For(State(null), showNumber: false, 70, 90));
    }

    [Fact]
    public void For_ZeroPercent_IsDistinctFromNullPercent()
    {
        var zero = TrayIconKey.For(State(0), showNumber: false, 70, 90);
        var missing = TrayIconKey.For(State(null), showNumber: false, 70, 90);
        Assert.Equal("0|-|70|90", zero);
        Assert.NotEqual(zero, missing);
    }

    [Fact]
    public void For_PercentOver100_IsNotClamped()
    {
        Assert.Equal("137|-|70|90", TrayIconKey.For(State(137), showNumber: false, 70, 90));
    }

    [Fact]
    public void For_NegativePercent_Roundtrips()
    {
        Assert.Equal("-5|-|70|90", TrayIconKey.For(State(-5), showNumber: false, 70, 90));
    }

    [Fact]
    public void For_SamePercent_DifferentWarnThreshold_DiffersBecauseColourChanges()
    {
        Assert.NotEqual(
            TrayIconKey.For(State(75), showNumber: false, 70, 90),
            TrayIconKey.For(State(75), showNumber: false, 80, 90));
    }

    [Fact]
    public void For_SamePercent_DifferentAlertThreshold_DiffersBecauseColourChanges()
    {
        Assert.NotEqual(
            TrayIconKey.For(State(95), showNumber: false, 70, 90),
            TrayIconKey.For(State(95), showNumber: false, 70, 99));
    }

    [Fact]
    public void For_IdenticalInputs_ProduceEqualKeys_SoRepaintIsSkipped()
    {
        Assert.Equal(
            TrayIconKey.For(State(42), showNumber: false, 70, 90),
            TrayIconKey.For(State(42), showNumber: false, 70, 90));
    }

    [Fact]
    public void For_IgnoresResetsAtAndPresent_WhichDoNotAffectThePixels()
    {
        var a = new LimitState(42, DateTimeOffset.UnixEpoch, true);
        var b = new LimitState(42, DateTimeOffset.UnixEpoch.AddYears(30), false);
        Assert.Equal(TrayIconKey.For(a, false, 70, 90), TrayIconKey.For(b, false, 70, 90));
    }

    [Fact]
    public void For_FieldsCannotBleedTogether_AdjacentValuesStayDistinct()
    {
        // Without a separator "1|70" and "17|0" would collide.
        Assert.NotEqual(
            TrayIconKey.For(State(1), showNumber: false, 70, 90),
            TrayIconKey.For(State(17), showNumber: false, 0, 90));
    }

    [Fact]
    public void For_IsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1234|-|70|90", TrayIconKey.For(State(1234), showNumber: false, 70, 90));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
