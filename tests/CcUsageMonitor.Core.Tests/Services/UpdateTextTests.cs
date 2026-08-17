using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor.Core.Tests.Services;

/// <summary>
/// Exact strings, so a careless edit to the wording is a test failure rather than a
/// silent change to what the user is told about their install.
/// </summary>
public class UpdateTextTests
{
    [Fact]
    public void Checking_IsTheInProgressLine()
    {
        Assert.Equal("Checking for updates…", UpdateText.Checking);
    }

    [Fact]
    public void StatusLine_UpToDate_NamesTheRunningVersion()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpToDate, new Version(1, 3, 0), "https://example.com");
        Assert.Equal("Up to date (1.3.0)", UpdateText.StatusLine(result, new Version(1, 3, 0)));
    }

    [Fact]
    public void StatusLine_UpdateAvailable_NamesTheNewVersion()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpdateAvailable, new Version(1, 4, 0), "https://example.com");
        Assert.Equal("Update available: 1.4.0", UpdateText.StatusLine(result, new Version(1, 3, 0)));
    }

    [Fact]
    public void StatusLine_Failed_SaysSoWithoutTechnicalDetail()
    {
        Assert.Equal("Couldn't check for updates", UpdateText.StatusLine(UpdateCheckResult.Failed(), new Version(1, 3, 0)));
    }

    [Fact]
    public void StatusLine_UpToDate_WithMissingLatestVersion_FallsBackToRunningVersion()
    {
        // Defensive: status says up to date, so the running version is the truthful thing
        // to show even if the payload's version went missing.
        var result = new UpdateCheckResult(UpdateStatus.UpToDate, null, null);
        Assert.Equal("Up to date (1.3.0)", UpdateText.StatusLine(result, new Version(1, 3, 0)));
    }

    [Fact]
    public void StatusLine_UpdateAvailable_WithMissingLatestVersion_DegradesToFailed()
    {
        // Claiming an update exists without being able to name it is worse than admitting
        // the check did not work.
        var result = new UpdateCheckResult(UpdateStatus.UpdateAvailable, null, null);
        Assert.Equal("Couldn't check for updates", UpdateText.StatusLine(result, new Version(1, 3, 0)));
    }

    [Fact]
    public void StatusLine_RendersThreeComponents_NotFourOrTwo()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpdateAvailable, new Version(2, 0, 0), "https://example.com");
        Assert.Equal("Update available: 2.0.0", UpdateText.StatusLine(result, new Version(1, 3, 0)));
    }

    [Fact]
    public void ActionLabel_UpdateAvailable_OffersTheNewVersion()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpdateAvailable, new Version(1, 4, 0), "https://example.com");
        Assert.Equal("Get 1.4.0", UpdateText.ActionLabel(result));
    }

    [Fact]
    public void ActionLabel_UpToDate_FallsBackToTheProjectLink()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpToDate, new Version(1, 3, 0), "https://example.com");
        Assert.Equal("View on GitHub", UpdateText.ActionLabel(result));
    }

    [Fact]
    public void ActionLabel_Failed_FallsBackToTheProjectLink()
    {
        Assert.Equal("View on GitHub", UpdateText.ActionLabel(UpdateCheckResult.Failed()));
    }
}

/// <summary>
/// The update check writes one log line per run; pin it so the log stays greppable.
/// </summary>
public class LogFormatUpdateCheckTests
{
    [Fact]
    public void UpdateCheck_UpdateAvailable_NamesTheVersion()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpdateAvailable, new Version(1, 4, 0), "https://example.com");
        Assert.Equal("update check · available · latest=1.4.0", LogFormat.UpdateCheck(result));
    }

    [Fact]
    public void UpdateCheck_UpToDate_NamesTheVersion()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpToDate, new Version(1, 3, 0), "https://example.com");
        Assert.Equal("update check · up to date · latest=1.3.0", LogFormat.UpdateCheck(result));
    }

    [Fact]
    public void UpdateCheck_Failed_HasNoVersionToken()
    {
        Assert.Equal("update check · failed", LogFormat.UpdateCheck(UpdateCheckResult.Failed()));
    }

    [Fact]
    public void UpdateCheck_UpToDateWithNullVersion_DoesNotEmitTheWordNull()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpToDate, null, null);
        Assert.Equal("update check · up to date", LogFormat.UpdateCheck(result));
    }
}
