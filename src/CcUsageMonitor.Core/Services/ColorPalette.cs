using System.Collections.Generic;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Color palette resolving a band + optional overrides to hex color strings.
/// Per spec §4.4 — defaults are fixed; colors section allows per-key overrides.
/// </summary>
public class ColorPalette
{
    private static readonly Dictionary<BandType, string> DefaultColors = new()
    {
        { BandType.Green, "#4CAF50" },
        { BandType.Yellow, "#FFC107" },
        { BandType.Red, "#F44336" },
        { BandType.Dim, "#757575" },
        { BandType.Track, "#222222" }
    };

    private readonly Dictionary<string, string> _overrides;

    public ColorPalette(IReadOnlyDictionary<string, string>? overrides = null)
    {
        _overrides = overrides != null ? new Dictionary<string, string>(overrides) : new Dictionary<string, string>();
    }

    /// <summary>Get the hex color for the given band, applying overrides.</summary>
    public string GetColor(BandType band)
    {
        var key = band.ToString().ToLowerInvariant();
        if (_overrides.TryGetValue(key, out var overrideColor))
            return overrideColor;
        return DefaultColors[band];
    }

    /// <summary>All default colors as a dictionary keyed by lowercase band name.</summary>
    public static IReadOnlyDictionary<string, string> Defaults => new Dictionary<string, string>(
        DefaultColors.Select(kv => new KeyValuePair<string, string>(kv.Key.ToString().ToLowerInvariant(), kv.Value))
    );
}
