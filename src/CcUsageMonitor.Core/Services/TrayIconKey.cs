using System.Globalization;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Identity of a rendered tray ring: two states with the same key produce a
/// pixel-identical bitmap, so the tray icon does not need to be reassigned.
///
/// This is the guard against the handle leak that crashed the app every few days.
/// Each icon assignment makes the Win32 backend build a native icon whose handles are
/// only reclaimed when the owning WindowIcon's platform impl is disposed; re-assigning
/// an unchanged ring on every poll burned handles for no visual benefit until the
/// process hit its 10,000-object quota.
///
/// Every input RingRenderer.Render reads must be represented here. Omitting one would
/// suppress a real visual change; including something it ignores only costs a redundant
/// (but correct) redraw.
/// </summary>
public static class TrayIconKey
{
    /// <summary>Culture-invariant so a comma decimal locale cannot collapse distinct keys.</summary>
    public static string For(LimitState state, bool showNumber, int warnThreshold, int alertThreshold)
    {
        var pct = state.Pct.HasValue
            ? state.Pct.Value.ToString(CultureInfo.InvariantCulture)
            : "-";
        return string.Concat(
            pct, "|",
            showNumber ? "n" : "-", "|",
            warnThreshold.ToString(CultureInfo.InvariantCulture), "|",
            alertThreshold.ToString(CultureInfo.InvariantCulture));
    }
}
