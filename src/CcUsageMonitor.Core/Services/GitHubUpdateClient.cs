using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Reads the project's latest GitHub release and compares it against the running version.
///
/// Deliberately unauthenticated: the repo is public and this only runs when the user picks
/// "Check for updates", so the 60-requests-per-hour anonymous limit is irrelevant. A rate
/// limit response is treated as an ordinary failure rather than a special case — there is
/// nothing useful to tell the user beyond "couldn't check".
/// </summary>
public class GitHubUpdateClient : IUpdateClient
{
    /// <summary>Excludes drafts and pre-releases, so a beta tag can never prompt an update.</summary>
    public const string LatestReleaseUrl = "https://api.github.com/repos/bitlamas/cc-usage-monitor/releases/latest";

    /// <summary>Shown when the release payload carries no html_url of its own.</summary>
    public const string ReleasesPageUrl = "https://github.com/bitlamas/cc-usage-monitor/releases";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;

    public GitHubUpdateClient() : this(null) { }

    /// <param name="httpClient">Injected for testing; a default client is created otherwise.</param>
    public GitHubUpdateClient(HttpClient? httpClient)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(Version running, CancellationToken ct)
    {
        try
        {
            // Bound the wait independently of the caller so a hung connection cannot leave
            // the About window sitting on "Checking…" forever.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            // GitHub rejects API requests without a User-Agent.
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("cc-usage-monitor", AppVersionString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Failed();

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Parse(body, running);
        }
        catch (Exception)
        {
            // Offline, DNS failure, timeout, cancellation, TLS problem — all the same to
            // the user, and none of them may escape into the UI thread.
            return UpdateCheckResult.Failed();
        }
    }

    private static UpdateCheckResult Parse(string body, Version running)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String ||
                !ReleaseVersion.TryParseTag(tagElement.GetString(), out var latest))
            {
                return UpdateCheckResult.Failed();
            }

            var url = root.TryGetProperty("html_url", out var urlElement) &&
                      urlElement.ValueKind == JsonValueKind.String &&
                      !string.IsNullOrWhiteSpace(urlElement.GetString())
                ? urlElement.GetString()!
                : ReleasesPageUrl;

            var status = ReleaseVersion.IsNewer(latest, running)
                ? UpdateStatus.UpdateAvailable
                : UpdateStatus.UpToDate;

            return new UpdateCheckResult(status, latest, url);
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed();
        }
    }

    private static string AppVersionString()
    {
        var v = typeof(GitHubUpdateClient).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
