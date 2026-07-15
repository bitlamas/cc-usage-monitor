using System;
using System.Globalization;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>Pure log-line and per-poll diagnostic formatters.</summary>
public static class LogFormat
{
    private const string Sep = " \u00B7 ";      // " · "

    /// <summary>A single log line, NO trailing newline.
    /// Format: "{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{LEVEL}] {message}"
    /// Uses CultureInfo.InvariantCulture for the timestamp.</summary>
    public static string Line(DateTimeOffset timestamp, LogLevel level, string message)
    {
        var tsStr = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var levelStr = level.ToString().ToUpperInvariant();
        return $"{tsStr} [{levelStr}] {message}";
    }

    /// <summary>The per-poll diagnostic message body.
    /// Limits render in FIXED order: 5h, weekly, fable.
    /// Pct token = "{pct}%" or "--%" when Pct is null.</summary>
    public static string Poll(UsageSnapshot snapshot)
    {
        var pct5h = PctToken(snapshot.Limits.TryGetValue(LimitKind.Session5h, out var s5) ? s5.Pct : null);
        var pctWk = PctToken(snapshot.Limits.TryGetValue(LimitKind.WeeklyAll, out var sw) ? sw.Pct : null);
        var pctFb = PctToken(snapshot.Limits.TryGetValue(LimitKind.WeeklyFable, out var sf) ? sf.Pct : null);

        if (snapshot.ErrorKind is null)
        {
            return $"poll ok{Sep}5h={pct5h}{Sep}weekly={pctWk}{Sep}fable={pctFb}";
        }

        var kindName = snapshot.ErrorKind.Value.ToString();
        return $"poll err{Sep}kind={kindName}{Sep}fails={snapshot.ConsecutiveFailures}{Sep}5h={pct5h}{Sep}weekly={pctWk}{Sep}fable={pctFb}";
    }

    private static string PctToken(int? pct) => pct is null ? "--%" : $"{pct}%";
}
