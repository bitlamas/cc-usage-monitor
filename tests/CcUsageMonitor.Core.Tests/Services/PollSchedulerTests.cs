using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class PollSchedulerTests
{
    [Theory]
    [InlineData(null, null, 180, 0, 180)]                        // A6.1
    [InlineData(ErrorKind.RateLimited, 300, 180, 1, 300)]        // A6.2
    [InlineData(ErrorKind.RateLimited, 0, 180, 1, 60)]           // A6.3 floor
    [InlineData(ErrorKind.RateLimited, null, 180, 1, 180)]       // A6.4 base
    [InlineData(ErrorKind.RateLimited, 9999, 180, 1, 300)]       // A6.5 ceiling
    [InlineData(ErrorKind.RateLimited, 45, 180, 1, 60)]          // A6.6 floor on small Retry-After
    [InlineData(ErrorKind.CliRequiredForReauth, null, 180, 1, 15)]   // A6.7
    [InlineData(ErrorKind.CliRequiredForReauth, null, 180, 2, 30)]   // A6.8
    [InlineData(ErrorKind.CliRequiredForReauth, null, 180, 3, 60)]   // A6.9
    [InlineData(ErrorKind.CliRequiredForReauth, null, 180, 4, 120)]  // A6.10
    [InlineData(ErrorKind.CliRequiredForReauth, null, 180, 5, 180)]  // A6.11 cap
    [InlineData(ErrorKind.CliRequiredForReauth, null, 180, 99, 180)] // A6.12 no overflow
    [InlineData(ErrorKind.NetworkError, null, 180, 0, 15)]       // A6.13 n=max(1,0)
    [InlineData(ErrorKind.NetworkError, 300, 180, 1, 15)]        // A6.14 retryAfter ignored
    [InlineData(ErrorKind.NotLoggedIn, null, 180, 1, 15)]        // A6.15
    [InlineData(ErrorKind.NetworkError, null, 10, 5, 10)]        // A6.16 base < FirstRetry
    [InlineData(ErrorKind.ServerError, null, 180, 2, 30)]        // A6.17
    public void NextDelaySeconds_AllCases(
        ErrorKind? kind, int? retryAfterSeconds, int baseIntervalSeconds, int consecutiveFailures, int expected)
        => Assert.Equal(expected, PollScheduler.NextDelaySeconds(kind, retryAfterSeconds, baseIntervalSeconds, consecutiveFailures));
}
