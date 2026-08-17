using System;
using System.Globalization;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Wording for the update check, kept out of the window so it can be pinned by tests.
/// </summary>
public static class UpdateText
{
    public const string Checking = "Checking for updates…";
    private const string CouldNotCheck = "Couldn't check for updates";
    private const string ViewOnGitHub = "View on GitHub";

    /// <summary>
    /// One line describing the outcome. An "update available" that cannot name the new
    /// version degrades to the failure wording — claiming an upgrade exists without saying
    /// which is worse than admitting the check did not work.
    /// </summary>
    public static string StatusLine(UpdateCheckResult result, Version running) => result.Status switch
    {
        UpdateStatus.UpToDate => $"Up to date ({Format(result.LatestVersion ?? running)})",
        UpdateStatus.UpdateAvailable when result.LatestVersion is not null
            => $"Update available: {Format(result.LatestVersion)}",
        _ => CouldNotCheck,
    };

    /// <summary>Label for the button beside the status line.</summary>
    public static string ActionLabel(UpdateCheckResult result) =>
        result.Status == UpdateStatus.UpdateAvailable && result.LatestVersion is not null
            ? $"Get {Format(result.LatestVersion)}"
            : ViewOnGitHub;

    private static string Format(Version v) =>
        string.Create(CultureInfo.InvariantCulture, $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}");
}
