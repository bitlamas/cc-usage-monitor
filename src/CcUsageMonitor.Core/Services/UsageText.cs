using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Pure formatting helpers — tooltip, toast, countdown, error strings.
/// Per spec §6 (formatting) + §7 (error tooltips). Exact-equality on all output.
/// </summary>
public static class UsageText
{
    // Exact glyphs from spec §6
    private const string MiddleDot = "\u00B7";        // ·
    private const string EmDash = "\u2014";            // —
    private const string Ellipsis = "\u2026";          // …
    private const string Resetting = "resetting" + Ellipsis;

    /// <summary>
    /// Countdown string for a ResetsAt, or null if ResetsAt is null.
    /// Returns "resetting…" when ResetsAt <= now. Otherwise formatted remaining.
    /// </summary>
    public static string? Countdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null)
            return null;

        if (resetsAt <= now)
            return Resetting;

        var remaining = resetsAt.Value - now;
        var totalMinutes = (int)remaining.TotalMinutes; // floor toward zero

        if (totalMinutes <= 0)
            // 0 < remaining < 1 minute → "0m"
            return remaining.TotalSeconds > 0 ? "0m" : Resetting;

        if (totalMinutes >= 60)
            return $"{totalMinutes / 60}h {totalMinutes % 60}m";

        return $"{totalMinutes}m";
    }

    /// <summary>
    /// Simplified tooltip: "{shortLabel} · {pct}% · {reset info}"
    /// Short label: "5-hour", "Weekly", "Weekly (Sonnet)", "Weekly (Opus)"
    /// Reset info: "Resets in ~{countdown}" | "Resetting" | "Updated {HH:mm}"
    /// </summary>
    public static string Tooltip(LimitKind kind, LimitState state, DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var shortLabel = ShortLabel(kind);
        var pct = state.Pct is null ? "--" : state.Pct.Value.ToString();
        var countdown = Countdown(state.ResetsAt, now);
        var updatedAtStr = updatedAt.ToString("HH:mm");

        string resetInfo;
        if (countdown is null)
        {
            resetInfo = $"Updated {updatedAtStr}";
        }
        else if (countdown == Resetting)
        {
            resetInfo = "Resetting";
        }
        else if ((state.ResetsAt!.Value - now).TotalHours >= 24)
        {
            // Far reset (weeklies): absolute local day + time, e.g. "Resets Sun 10AM".
            resetInfo = $"Resets {FormatAbsoluteReset(state.ResetsAt.Value, now)}";
        }
        else
        {
            resetInfo = $"Resets in {countdown}";
        }

        return $"{shortLabel} · {pct}% · {resetInfo}";
    }

    private static string ShortLabel(LimitKind kind) => kind switch
    {
        LimitKind.Session5h => "5-hour",
        LimitKind.WeeklyAll => "Weekly",
        LimitKind.WeeklySonnet => "Weekly (Sonnet)",
        LimitKind.WeeklyOpus => "Weekly (Opus)",
        _ => LimitKindMapping.GetLabel(kind)
    };

    /// <summary>
    /// Absolute reset time for far-out resets (>= 24h): abbreviated local day + 12-hour time,
    /// e.g. "Sun 10AM" / "Mon 10:30PM". Formatted in now.Offset so it is the user's local
    /// day/time and deterministic under test.
    /// </summary>
    private static string FormatAbsoluteReset(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var local = resetsAt.ToOffset(now.Offset);
        var day = local.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture);
        var time = local.Minute == 0
            ? local.ToString("htt", System.Globalization.CultureInfo.InvariantCulture)
            : local.ToString("h:mmtt", System.Globalization.CultureInfo.InvariantCulture);
        return $"{day} {time}";
    }

    /// <summary>Toast title: "Claude usage at {pct}%". Pct is unclamped.</summary>
    public static string ToastTitle(int pct)
    {
        return $"Claude usage at {pct}%";
    }

    /// <summary>Toast body: future="{label} resets in {countdown}", past/null="{label} is resetting" or "{label}".</summary>
    public static string ToastBody(LimitKind kind, DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        var label = LimitKindMapping.GetLabel(kind);
        var countdown = Countdown(resetsAt, now);

        if (countdown is null)
            return label;

        if (countdown == Resetting)
            return $"{label} is resetting";

        return $"{label} resets in {countdown}";
    }

    /// <summary>Error-state tooltip per spec §7 exact strings.</summary>
    public static string ErrorTooltip(ErrorKind kind) => kind switch
    {
        ErrorKind.NotLoggedIn => "Not logged in to Claude Code",
        ErrorKind.CliRequiredForReauth => "Claude Code CLI required for re-auth",
        ErrorKind.NetworkError => "Network error — data may be stale",
        _ => "Unknown error"
    };
}
