using SkiaSharp;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Pure SkiaSharp renderer: percentage → circular progress-ring bitmap.
/// Per spec §4.4 — no I/O, deterministic, unit-testable.
/// </summary>
public static class RingRenderer
{
    /// <summary>
    /// Render a circular progress-ring bitmap for the given percentage.
    /// </summary>
    /// <param name="pct">Percentage (0–∞, clamped to [0,100] for band/sweep; null → dim track-only).</param>
    /// <param name="warnThreshold">Warning threshold (default 70). Band is selected from clamped pct.</param>
    /// <param name="alertThreshold">Alert threshold (default 90). Band is selected from clamped pct.</param>
    /// <param name="showNumber">Reserved — no text is drawn inside the ring in v1.</param>
    /// <param name="sizePx">Bitmap size in logical pixels (default 32).</param>
    /// <returns>A 32×32 (or sizePx×sizePx) PNG-encoded SKBitmap.</returns>
    public static SKBitmap Render(int? pct, int warnThreshold, int alertThreshold, bool showNumber, int sizePx = 32)
    {
        var half = sizePx / 2f;
        var radius = half - 1f; // 1px margin from edge
        var rect = new SKRect(half - radius, half - radius, half + radius, half + radius);

        var bitmap = new SKBitmap(sizePx, sizePx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // --- Track (transparent fill + thin dark outline ring) ---
        using var trackOutlinePaint = new SKPaint
        {
            Color = new SKColor(0x00, 0x00, 0x00, 0xCC),
            StrokeWidth = 1f,
            IsStroke = true
        };
        canvas.DrawCircle(half, half, radius, trackOutlinePaint);

        // --- Wedge (filled pie slice, band-colored) ---
        if (pct is not null)
        {
            var clamped = Math.Max(0, Math.Min(100, pct.Value));
            var band = Bands.Select(pct, warnThreshold, alertThreshold);
            var colorPalette = new ColorPalette();
            if (band is not BandType.Dim)
            {
                var colorHex = colorPalette.GetColor(band);
                var colorArgb = HexToArgb(colorHex);
                using var wedgePaint = new SKPaint
                {
                    Color = new SKColor(colorArgb),
                    IsStroke = false
                };

                // Wedge sweep: clamped pct → 0° to 360° (full disc at >=100%)
                var sweepAngle = clamped / 100f * 360f;
                // Start at -90° (12 o'clock), sweep clockwise, useCenter=true for filled pie
                canvas.DrawArc(rect, -90f, sweepAngle, true, wedgePaint);
            }
        }
        // else: null pct → transparent disc only (no wedge)

        return bitmap;
    }

    private static uint HexToArgb(string hex)
    {
        // Parse "#RRGGBB" → 0xFFRRGGBB
        var r = byte.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
        var g = byte.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
        var b = byte.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
        return ((uint)0xFF << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }
}
