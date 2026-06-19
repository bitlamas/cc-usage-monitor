namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Color-band selection per spec §4.4.
/// Band is selected from the clamped pct; null pct → Dim.
/// </summary>
public static class Bands
{
    /// <summary>
    /// Select the color band for the given percentage.
    /// pct is clamped to [0,100] before evaluation; null → Dim.
    /// </summary>
    public static BandType Select(int? pct, int warnThreshold, int alertThreshold)
    {
        if (pct is null)
            return BandType.Dim;

        var clamped = Clamp(pct.Value);

        if (clamped < warnThreshold)
            return BandType.Green;
        if (clamped < alertThreshold)
            return BandType.Yellow;
        return BandType.Red;
    }

    private static int Clamp(int value) => Math.Max(0, Math.Min(100, value));
}
