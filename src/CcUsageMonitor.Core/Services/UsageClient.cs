using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// HTTP-based usage client that fetches from the Anthropic API.
/// Per spec §4.2: bodyless GET, no Content-Type, 401-refresh-retry-once.
/// </summary>
public class UsageClient : IUsageClient
{
    private static readonly string ApiBaseUrl = "https://api.anthropic.com";
    private static readonly string Endpoint = "/api/oauth/usage";
    private static readonly string BetaTag = "api-usage-2026-05-29";

    private readonly ICredentialStore _credentialStore;
    private readonly string _betaTag;
    private readonly string _version;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates a UsageClient with the default HttpClient and beta tag.
    /// </summary>
    /// <param name="credentialStore">Used for token refresh on 401.</param>
    public UsageClient(ICredentialStore credentialStore)
        : this(credentialStore, BetaTag) { }

    /// <summary>
    /// Creates a UsageClient with a custom HttpClient and beta tag (for testing).
    /// </summary>
    /// <param name="credentialStore">Used for token refresh on 401.</param>
    /// <param name="betaTag">The anthropic-beta header value.</param>
    /// <param name="httpClient">The HttpClient to use (or null for a new one).</param>
    public UsageClient(ICredentialStore credentialStore, string betaTag, HttpClient? httpClient = null)
    {
        _credentialStore = credentialStore;
        _betaTag = betaTag;
        _version = typeof(UsageClient).Assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <inheritdoc />
    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        var token = _credentialStore.GetAccessToken();
        if (token is null)
        {
            return new UsageSnapshot(
                BuildEmptySnapshot(),
                DateTimeOffset.UtcNow,
                Error: "Not logged in to Claude Code",
                Stale: true);
        }

        var result = await TryFetch(token, ct).ConfigureAwait(false);

        // On 401: refresh once and retry
        if (result.Error != null && result.Error.Contains("401"))
        {
            try
            {
                await _credentialStore.RefreshAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Refresh failure — still return the error snapshot
            }

            var renewedToken = _credentialStore.GetAccessToken();
            if (renewedToken is not null)
            {
                result = await TryFetch(renewedToken, ct).ConfigureAwait(false);
            }
            // If token is still null after refresh, return the original error
        }

        return result;
    }

    private async Task<UsageSnapshot> TryFetch(string token, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ApiBaseUrl + Endpoint);

        // Per spec §4.2: NO Content-Type header on bodyless GET
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", _betaTag);
        request.Headers.UserAgent.ParseAdd($"cc-usage-monitor/{_version}");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new UsageSnapshot(
                BuildEmptySnapshot(),
                DateTimeOffset.UtcNow,
                Error: SanitizeError(ex.Message),
                Stale: true);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new UsageSnapshot(
                BuildEmptySnapshot(),
                DateTimeOffset.UtcNow,
                Error: "HTTP 401 after refresh",
                Stale: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new UsageSnapshot(
                BuildEmptySnapshot(),
                DateTimeOffset.UtcNow,
                Error: $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                Stale: true);
        }

        try
        {
            var snapshot = UsageParser.Parse(body);
            return snapshot;
        }
        catch
        {
            return new UsageSnapshot(
                BuildEmptySnapshot(),
                DateTimeOffset.UtcNow,
                Error: "Failed to parse usage response",
                Stale: true);
        }
    }

    private static IReadOnlyDictionary<LimitKind, LimitState> BuildEmptySnapshot()
    {
        var dict = new Dictionary<LimitKind, LimitState>();
        foreach (var kind in LimitKindMapping.AllKinds)
            dict[kind] = new LimitState(null, null, Present: false);
        return dict;
    }

    /// <summary>
    /// Sanitizes error messages to never include the OAuth token.
    /// Per spec §7.1: Error must never contain a token-shaped value.
    /// </summary>
    private static string SanitizeError(string error)
    {
        // Remove anything that looks like an OAuth token
        return System.Text.RegularExpressions.Regex.Replace(error, @"sk-ant-[a-zA-Z0-9\-_]{10,}", "[REDACTED]");
    }
}
