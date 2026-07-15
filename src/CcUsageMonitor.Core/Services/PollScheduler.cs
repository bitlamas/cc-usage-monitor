namespace CcUsageMonitor.Core.Services;

/// <summary>Poll-interval scheduler: 429 back-off + fast error-retry ladder.</summary>
public static class PollScheduler
{
    public const int FirstRetrySeconds = 15;   // first fast retry after an error
    public const int RateLimitFloor    = 60;   // 429 min back-off
    public const int RateLimitCeiling  = 300;  // 429 max back-off

    /// <summary>
    /// Computes the delay (in seconds) before the next poll.
    /// </summary>
    public static int NextDelaySeconds(
        ErrorKind? kind, int? retryAfterSeconds, int baseIntervalSeconds, int consecutiveFailures)
    {
        // Success → base interval
        if (kind is null)
            return baseIntervalSeconds;

        // RateLimited → clamp retryAfter (or base) to [floor, ceiling]
        if (kind == ErrorKind.RateLimited)
        {
            return Math.Clamp(retryAfterSeconds ?? baseIntervalSeconds, RateLimitFloor, RateLimitCeiling);
        }

        // Any other error → fast exponential backoff (retryAfter ignored)
        long n = Math.Max(1, consecutiveFailures);
        long delay = FirstRetrySeconds;
        for (long i = 1; i < n; i++)
        {
            delay *= 2;
            if (delay >= baseIntervalSeconds)
                break;
        }
        return (int)Math.Min(delay, baseIntervalSeconds);
    }
}
