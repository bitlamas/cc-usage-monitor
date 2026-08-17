using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Parses a GitHub release tag into a comparable version, and decides whether it is newer
/// than what is running. Pure — no I/O — so the decision that drives "update available"
/// can be tested exhaustively.
/// </summary>
public static class ReleaseVersion
{
    private const int Components = 3;   // releases are major.minor.patch

    /// <summary>
    /// Parses tags of the form "v1.3.0", "1.3.0", "v1.3" or "v1.4.0-beta.1".
    /// Missing components are zero-filled and any fourth component is dropped, so every
    /// result has exactly three parts and comparisons are never lopsided (Version treats
    /// an unspecified component as -1, which would rank "1.3" below "1.3.0").
    /// Returns false for anything not of that shape rather than guessing.
    /// </summary>
    public static bool TryParseTag(string? tag, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            text = text[1..];

        // Drop any pre-release ("-beta.1") or build metadata ("+abc123") suffix and
        // compare on the numeric core. /releases/latest already excludes pre-releases,
        // so this only ever fires defensively.
        var cut = text.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
            text = text[..cut];

        if (text.Length == 0)
            return false;

        var parts = text.Split('.');
        var numbers = new int[Components];
        for (var i = 0; i < parts.Length && i < Components; i++)
        {
            // int.TryParse would accept a leading '-'; require plain digits so "1.-3.0"
            // and other malformed tags are rejected outright.
            var part = parts[i];
            if (part.Length == 0)
                return false;
            foreach (var c in part)
            {
                if (c < '0' || c > '9')
                    return false;
            }
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;   // out of int range
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    /// <summary>
    /// True only when <paramref name="latest"/> is strictly greater than
    /// <paramref name="running"/>. Equal versions and yanked/older releases never prompt.
    /// </summary>
    public static bool IsNewer(Version latest, Version running) => latest > running;
}
