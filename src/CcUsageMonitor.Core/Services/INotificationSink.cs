namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Cross-platform notification sink for toast/balloon messages.
/// Per spec §4.6: TryShow returns false when no sink is available,
/// but the AlertManager still records the alert as fired.
/// </summary>
public interface INotificationSink
{
    /// <summary>
    /// Attempts to show a toast notification. Returns false if unavailable.
    /// </summary>
    /// <param name="title">Toast title.</param>
    /// <param name="body">Toast body.</param>
    /// <returns>True if the toast was shown, false if unavailable.</returns>
    bool TryShow(string title, string body);
}
