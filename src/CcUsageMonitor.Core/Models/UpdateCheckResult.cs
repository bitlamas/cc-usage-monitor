using System;

namespace CcUsageMonitor.Core.Models;

/// <summary>Outcome of a check against the project's latest GitHub release.</summary>
public enum UpdateStatus
{
    /// <summary>The running version is the latest published release, or newer.</summary>
    UpToDate,

    /// <summary>A strictly newer release exists.</summary>
    UpdateAvailable,

    /// <summary>The check could not be completed — offline, rate-limited, or a bad response.</summary>
    Failed,
}

/// <summary>
/// Result of an update check. On <see cref="UpdateStatus.Failed"/> both other fields are
/// null: the caller has nothing truthful to show beyond "couldn't check".
/// </summary>
public record UpdateCheckResult(UpdateStatus Status, Version? LatestVersion, string? ReleaseUrl)
{
    public static UpdateCheckResult Failed() => new(UpdateStatus.Failed, null, null);
}
