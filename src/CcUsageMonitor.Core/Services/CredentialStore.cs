using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Reads ~/.claude/.credentials.json, exposes the access token, and refreshes via the CLI.
/// Per spec §4.1: before/after expiresAt comparison (long epoch ms) is the sole success signal.
/// </summary>
public class CredentialStore : ICredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IProcessRunner _processRunner;
    private readonly string _credentialsPath;
    private readonly string? _claudeExeName; // for test injection

    /// <summary>
    /// Creates a CredentialStore that uses the default credentials path and a real ProcessRunner.
    /// </summary>
    public CredentialStore()
        : this(new ProcessRunner()) { }

    /// <summary>
    /// Creates a CredentialStore with an explicit process runner (for testing).
    /// </summary>
    /// <param name="processRunner">The process runner to use for CLI invocation.</param>
    /// <param name="credentialsPath">Override the default credentials path (for testing).</param>
    public CredentialStore(IProcessRunner processRunner, string? credentialsPath = null)
    {
        _processRunner = processRunner;
        _credentialsPath = credentialsPath ?? ResolveCredentialsPath();
        _claudeExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "claude.cmd" : "claude";
    }

    /// <inheritdoc />
    public string? GetAccessToken()
    {
        if (!File.Exists(_credentialsPath))
            return null;

        try
        {
            var json = File.ReadAllText(_credentialsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("claudeAiOauth", out var oauth) ||
                !oauth.TryGetProperty("accessToken", out var token))
                return null;

            return token.GetString();
        }
        catch
        {
            // Corrupt or unreadable → null
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        // Capture expiresAt BEFORE refresh (as a long epoch ms)
        long before = GetExpiresAtMs();

        // Run the CLI refresh command (stdout/stderr discarded, 30s timeout)
        var exitCode = await _processRunner.RunAsync(
            _claudeExeName ?? "claude",
            "-p hi --max-budget-usd 0.01",
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        // If process timed out (exit code -1), do NOT re-read — refresh failed
        if (exitCode == -1)
            return false;

        // Re-read and capture expiresAt AFTER refresh
        long after = GetExpiresAtMs();

        // Success iff strictly greater (spec §4.1 trap)
        return after > before;
    }

    private static string ResolveCredentialsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", ".credentials.json");
    }

    private long GetExpiresAtMs()
    {
        if (!File.Exists(_credentialsPath))
            return 0;

        try
        {
            var json = File.ReadAllText(_credentialsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("claudeAiOauth", out var oauth) ||
                !oauth.TryGetProperty("expiresAt", out var expiresAt))
                return 0;

            // expiresAt is a JSON number (epoch ms) — read as long
            if (expiresAt.ValueKind == JsonValueKind.Number &&
                expiresAt.TryGetInt64(out var ms))
                return ms;

            return 0;
        }
        catch
        {
            return 0;
        }
    }
}
