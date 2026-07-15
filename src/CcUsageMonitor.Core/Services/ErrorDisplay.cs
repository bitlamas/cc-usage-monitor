namespace CcUsageMonitor.Core.Services;

/// <summary>"Silent until persistent" error display gating.</summary>
public static class ErrorDisplay
{
    public const int SettleThreshold = 3;  // consecutive failures before a re-auth message is shown

    /// <summary>
    /// Returns true if the error should be surfaced to the user.
    /// Rules: null → false; transient → false; NotLoggedIn → true; CliRequiredForReauth → consecutiveFailures >= SettleThreshold.
    /// </summary>
    public static bool ShouldSurface(ErrorKind? kind, int consecutiveFailures)
    {
        if (kind is null)
            return false;

        if (ErrorPolicy.IsTransient(kind.Value))
            return false;

        if (kind.Value == ErrorKind.NotLoggedIn)
            return true;

        // CliRequiredForReauth (and any other non-transient, non-NotLoggedIn)
        return consecutiveFailures >= SettleThreshold;
    }
}
