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
    public static SKBitmap Render(int? pct, int warnThreshold, int alertThreshold, bool showNumber, int sizePx = 32)
    {
        // Track the last drawn text for the seam
        LastDrawnText = null;

        var half = sizePx / 2f;
        var radius = half - 1f; // 1px margin from edge
        var rect = new SKRect(half - radius, half - radius, half + radius, half + radius);
        var strokeWidth = 3f;
        var halfStroke = strokeWidth / 2f;
        var adjustedRect = new SKRect(half - radius + halfStroke, half - radius + halfStroke,
                                       half + radius - halfStroke, half + radius - halfStroke);

        var bitmap = new SKBitmap(sizePx, sizePx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // --- Track (dim gray background circle) ---
        using var trackPaint = new SKPaint
        {
            Color = new SKColor(0x22, 0x22, 0x22),
            StrokeWidth = strokeWidth,
            IsStroke = true,
            StrokeCap = SKStrokeCap.Square
        };
        canvas.DrawCircle(half, half, radius, trackPaint);

        // --- Arc (foreground, colored by band) ---
        if (pct is not null)
        {
            var clamped = Math.Max(0, Math.Min(100, pct.Value));
            var band = Bands.Select(pct, warnThreshold, alertThreshold);
            var colorPalette = new ColorPalette();
            if (band is not BandType.Dim)
            {
                var colorHex = colorPalette.GetColor(band);
                var colorArgb = HexToArgb(colorHex);
                using var arcPaint = new SKPaint
                {
                    Color = new SKColor(colorArgb),
                    StrokeWidth = strokeWidth,
                    IsStroke = true,
                    StrokeCap = SKStrokeCap.Square
                };

                // Arc sweep: clamped pct → 0° to 360° (full circle at >=100%)
                var sweepAngle = clamped / 100f * 360f;
                // Start at -90° (12 o'clock), sweep clockwise
                canvas.DrawArc(adjustedRect, -90f, sweepAngle, false, arcPaint);
            }

            // --- Number (only when showNumber is true and pct is not null) ---
            if (showNumber)
            {
                var text = pct.Value.ToString();
                LastDrawnText = text;

                using var textPaint = new SKPaint
                {
                    TextSize = 14f,
                    IsStroke = false,
                    Color = new SKColor(0xFF, 0xFF, 0xFF)
                };
                textPaint.TextAlign = SKTextAlign.Center;
                // Center text horizontally and vertically in the bitmap
                canvas.DrawText(text, half, half + 5f, textPaint);
            }
        }
        // else: null pct → dim track-only (no arc, no number)

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
