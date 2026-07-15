using System.IO;
using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class LogPathsTests
{
    [Fact] // A4.10
    public void LogFile_Exact()
    {
        var dir = Path.Combine("x", "cc-usage-monitor");
        Assert.Equal(Path.Combine(dir, "logs", "cc-usage-monitor.log"), LogPaths.LogFile(dir));
    }

    [Fact] // A4.11
    public void RolledFile_Exact()
    {
        var dir = Path.Combine("x", "cc-usage-monitor");
        Assert.Equal(Path.Combine(dir, "logs", "cc-usage-monitor.log.1"), LogPaths.RolledFile(dir));
    }

    [Theory] // A4.12–A4.15
    [InlineData(4_999_999, 5_000_000, false)]
    [InlineData(5_000_000, 5_000_000, true)]
    [InlineData(5_000_001, 5_000_000, true)]
    [InlineData(0, 5_000_000, false)]
    public void ShouldRoll_Boundary(long currentSizeBytes, long maxSizeBytes, bool expected)
    {
        Assert.Equal(expected, LogRotation.ShouldRoll(currentSizeBytes, maxSizeBytes));
    }
}
