namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Error taxonomy classifier — transient (benign, auto-recovering) vs actionable (user must act).
/// </summary>
public static class ErrorPolicy
{
    /// <summary>Returns true if this error is transient (benign, auto-recovering → UI stays silent).</summary>
    public static bool IsTransient(ErrorKind kind) =>
        kind is ErrorKind.RateLimited or ErrorKind.NetworkError or ErrorKind.ServerError;
}
