using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class ErrorPolicyTests
{
    [Theory]
    [InlineData(ErrorKind.RateLimited, true)]
    [InlineData(ErrorKind.NetworkError, true)]
    [InlineData(ErrorKind.ServerError, true)]
    [InlineData(ErrorKind.NotLoggedIn, false)]
    [InlineData(ErrorKind.CliRequiredForReauth, false)]
    public void IsTransient_ClassifiesEachKind(ErrorKind kind, bool expected)
        => Assert.Equal(expected, ErrorPolicy.IsTransient(kind));
}

public class UsageSnapshotBackCompatTests
{
    [Fact]
    public void UsageSnapshot_4ArgConstruction_YieldsNullErrorKindAndRetryAfter()
    {
        var limits = new Dictionary<LimitKind, LimitState>
        {
            { LimitKind.Session5h, new LimitState(42, DateTimeOffset.UtcNow, true) }
        };
        var snapshot = new UsageSnapshot(limits, DateTimeOffset.UtcNow, null, false);
        Assert.Null(snapshot.ErrorKind);
        Assert.Null(snapshot.RetryAfterSeconds);
    }
}
