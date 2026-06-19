using System.Threading;
using System.Threading.Tasks;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Reads the Claude Code credentials file and manages token refresh.
/// Per spec §4.1: locates ~/.claude/.credentials.json, exposes the OAuth token,
/// and triggers a CLI-based refresh.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Gets the current OAuth access token, or null if unavailable.</summary>
    string? GetAccessToken();

    /// <summary>
    /// Triggers a token refresh via the Claude Code CLI.
    /// Success is determined by comparing expiresAt before and after (strictly greater).
    /// On timeout, the file is NOT re-read — the refresh is considered failed.
    /// </summary>
    Task<bool> RefreshAsync(CancellationToken ct);
}
