using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Internal result type for TryFetch — decouples HTTP status from the parsed snapshot.
/// </summary>
internal readonly record struct FetchResult(UsageSnapshot Snapshot, HttpStatusCode StatusCode);

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
                Stale: true,
                ErrorKind: ErrorKind.NotLoggedIn);
        }

        var result = await TryFetch(token, ct).ConfigureAwait(false);

        // On 401: refresh once and retry (branch on typed status, not message)
        if (result.StatusCode == HttpStatusCode.Unauthorized)
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

        // Classify the 401-after-refresh snapshot
        if (result.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new UsageSnapshot(
                result.Snapshot.Limits,
                result.Snapshot.UpdatedAt,
                Error: "HTTP 401 after refresh",
                Stale: true,
                ErrorKind: ErrorKind.CliRequiredForReauth,
                RetryAfterSeconds: null);
        }

        return result.Snapshot;
    }

    private async Task<FetchResult> TryFetch(string token, CancellationToken ct)
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
            // Transport failure — set NetworkError at the catch site
            return new FetchResult(new UsageSnapshot(
                BuildEmptySnapshot(),
                DateTimeOffset.UtcNow,
                Error: SanitizeError(ex.Message),
                Stale: true,
                ErrorKind: ErrorKind.NetworkError), HttpStatusCode.ServiceUnavailable);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = response.StatusCode == HttpStatusCode.Unauthorized
                ? "HTTP 401 after refresh"
                : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            ErrorKind kind = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => ErrorKind.RateLimited,
                _ => ErrorKind.ServerError,
            };
            return new FetchResult(new UsageSnapshot(
                BuildEmptySnapshot(),
                DateTimeOffset.UtcNow,
                Error: error,
                Stale: true,
                ErrorKind: kind,
                RetryAfterSeconds: kind == ErrorKind.RateLimited ? ParseRetryAfter(response.Headers) : null), response.StatusCode);
        }

        try
        {
            var snapshot = UsageParser.Parse(body);
            return new FetchResult(snapshot, response.StatusCode);
        }
        catch
        {
            return new FetchResult(new UsageSnapshot(
                BuildEmptySnapshot(),
                DateTimeOffset.UtcNow,
                Error: "Failed to parse usage response",
                Stale: true,
                ErrorKind: ErrorKind.ServerError), HttpStatusCode.InternalServerError);
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
    /// Parses the Retry-After header as delta-seconds only.
    /// HTTP-date or non-integer forms return null.
    /// </summary>
    private static int? ParseRetryAfter(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        // .NET's RetryConditionHeaderValue only exposes Date (HTTP-date).
        // Delta-seconds form is not exposed as a separate property —
        // parse the raw header value manually.
        if (headers.TryGetValues("Retry-After", out var vals))
        {
            foreach (var v in vals)
            {
                if (int.TryParse(v, out var n))
                    return n;
            }
        }
        return null;
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
