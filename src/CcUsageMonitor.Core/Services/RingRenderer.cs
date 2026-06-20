using SkiaSharp;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Pure SkiaSharp renderer: percentage → circular progress-ring bitmap.
/// Per spec §4.4 — no I/O, deterministic, unit-testable.
/// </summary>
public static class RingRenderer
{
    /// <summary>
    /// The exact text string passed to the last DrawText call.
    /// Null when showNumber was false or pct was null on the last Render call.
    /// Exposed as a seam so tests can assert the drawn text without rendering to disk.
    /// </summary>
    public static string? LastDrawnText { get; private set; }

    /// <summary>
    /// Render a circular progress-ring bitmap for the given percentage.
    /// </summary>
    /// <param name="pct">Percentage (0–∞, clamped to [0,100] for band/sweep; null → dim track-only).</param>
    /// <param name="warnThreshold">Warning threshold (default 70). Band is selected from clamped pct.</param>
    /// <param name="alertThreshold">Alert threshold (default 90). Band is selected from clamped pct.</param>
    /// <param name="showNumber">Whether to draw the percentage number inside the ring.</param>
    /// <param name="sizePx">Bitmap size in logical pixels (default 32).</param>
    /// <returns>A 32×32 (or sizePx×sizePx) PNG-encoded SKBitmap.</returns>

    /// <summary>
    /// Outline thickness (in px) for the dark halo drawn behind the % number.
    /// </summary>
    public const int TextOutlineThickness = 1;

    public static SKBitmap Render(int? pct, int warnThreshold, int alertThreshold, bool showNumber, int sizePx = 32)
    {
        // Track the last drawn text for the seam
        LastDrawnText = null;

        var half = sizePx / 2f;
        var radius = half - 1f; // 1px margin from edge
        var rect = new SKRect(half - radius, half - radius, half + radius, half + radius);

        var bitmap = new SKBitmap(sizePx, sizePx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // --- Track (transparent — only the wedge is visible) ---
        // A transparent disc ensures the bitmap has an opaque alpha fill for composited tray icons.
        using var trackPaint = new SKPaint
        {
            Color = SKColors.Transparent,
            IsStroke = false
        };
        canvas.DrawCircle(half, half, radius, trackPaint);

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

            // --- Number (only when showNumber is true and pct is not null) ---
            if (showNumber)
            {
                var text = pct.Value.ToString();
                LastDrawnText = text;

                var textPaint = new SKPaint
                {
                    TextSize = 14f,
                    IsStroke = false,
                    Color = new SKColor(0xFF, 0xFF, 0xFF)
                };
                textPaint.TextAlign = SKTextAlign.Center;

                // Vertically center via font metrics: baselineY = centerY - (Ascent + Descent) / 2
                var metrics = textPaint.FontMetrics;
                var baselineY = half - (metrics.Ascent + metrics.Descent) / 2f;

                // Draw a dark semi-transparent outline behind the white text
                using var outlinePaint = new SKPaint
                {
                    TextSize = textPaint.TextSize,
                    IsStroke = true,
                    StrokeWidth = TextOutlineThickness,
                    Color = new SKColor(0x00, 0x00, 0x00, 0xCC)
                };
                outlinePaint.TextAlign = SKTextAlign.Center;
                canvas.DrawText(text, half, baselineY, outlinePaint);

                // White text on top
                canvas.DrawText(text, half, baselineY, textPaint);
            }
        }
        // else: null pct → dim disc only (no wedge, no number)

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
