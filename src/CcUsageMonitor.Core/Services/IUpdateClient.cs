using System;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>Checks whether a newer release of the app has been published.</summary>
public interface IUpdateClient
{
    /// <summary>
    /// Compares <paramref name="running"/> against the latest published release.
    /// Never throws: every failure is reported as <see cref="UpdateStatus.Failed"/>.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(Version running, CancellationToken ct);
}
