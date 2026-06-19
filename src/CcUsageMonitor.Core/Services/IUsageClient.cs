using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Fetches usage data from the Anthropic API and returns a parsed UsageSnapshot.
/// Per spec §4.2: bodyless GET, no Content-Type header, 401-refresh-retry-once.
/// </summary>
public interface IUsageClient
{
    /// <summary>
    /// Fetches the latest usage snapshot. On 401, performs one refresh and retry.
    /// </summary>
    Task<UsageSnapshot> FetchAsync(CancellationToken ct);
}
