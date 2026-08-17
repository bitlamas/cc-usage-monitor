using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class ErrorDisplayTests
{
    [Theory]
    [InlineData(null, 5, false)]                                   // A7.1
    [InlineData(ErrorKind.RateLimited, 99, false)]                 // A7.2
    [InlineData(ErrorKind.NetworkError, 99, false)]                // A7.3
    [InlineData(ErrorKind.ServerError, 99, false)]                 // A7.4
    [InlineData(ErrorKind.NotLoggedIn, 0, true)]                   // A7.5
    [InlineData(ErrorKind.NotLoggedIn, 1, true)]                   // A7.6
    [InlineData(ErrorKind.CliRequiredForReauth, 0, false)]         // A7.7
    [InlineData(ErrorKind.CliRequiredForReauth, 2, false)]         // A7.8
    [InlineData(ErrorKind.CliRequiredForReauth, 3, true)]          // A7.9
    [InlineData(ErrorKind.CliRequiredForReauth, 4, true)]          // A7.10
    public void ShouldSurface_AllCases(ErrorKind? kind, int consecutiveFailures, bool expected)
        => Assert.Equal(expected, ErrorDisplay.ShouldSurface(kind, consecutiveFailures));
}
