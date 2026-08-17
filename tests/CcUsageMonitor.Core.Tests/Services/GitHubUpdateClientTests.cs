using System.Net;
using System.Text;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor.Core.Tests.Services;

/// <summary>
/// The update check touches the network, so every failure mode must land on
/// UpdateStatus.Failed rather than throwing — this runs behind a tray menu item in an app
/// that has already been crashed once by an unguarded UI path.
/// </summary>
public class GitHubUpdateClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag, string url = "https://github.com/bitlamas/cc-usage-monitor/releases/tag/v1.4.0")
        => $$"""{"tag_name":"{{tag}}","html_url":"{{url}}"}""";

    private static GitHubUpdateClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHandler handler)
    {
        handler = new StubHandler(responder);
        return new GitHubUpdateClient(new HttpClient(handler));
    }

    // ---- happy paths ----

    [Fact]
    public async Task CheckAsync_NewerRelease_ReportsUpdateAvailableWithVersionAndUrl()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, ReleaseJson("v1.4.0")), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(1, 4, 0), result.LatestVersion);
        Assert.Equal("https://github.com/bitlamas/cc-usage-monitor/releases/tag/v1.4.0", result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckAsync_SameVersion_ReportsUpToDate()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, ReleaseJson("v1.3.0")), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Equal(new Version(1, 3, 0), result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_OlderRelease_ReportsUpToDate_NeverPromptsDowngrade()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, ReleaseJson("v1.2.0")), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    // ---- request shape ----

    [Fact]
    public async Task CheckAsync_RequestsTheLatestReleaseEndpoint()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, ReleaseJson("v1.3.0")), out var handler);

        await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(
            "https://api.github.com/repos/bitlamas/cc-usage-monitor/releases/latest",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
    }

    [Fact]
    public async Task CheckAsync_SendsUserAgent_BecauseGitHubRejectsRequestsWithout()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, ReleaseJson("v1.3.0")), out var handler);

        await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.True(handler.LastRequest!.Headers.UserAgent.Count > 0);
    }

    // ---- failure modes ----

    [Fact]
    public async Task CheckAsync_RateLimited403_ReportsFailed()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}"""), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_NotFound404_ReportsFailed()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_MalformedJson_ReportsFailed()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, "not json at all"), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_MissingTagName_ReportsFailed()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, """{"html_url":"https://example.com"}"""), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UnparseableTag_ReportsFailed()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, ReleaseJson("nightly")), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_NetworkException_ReportsFailed_DoesNotThrow()
    {
        var client = ClientFor(_ => throw new HttpRequestException("no network"), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_AlreadyCancelledToken_ReportsFailed_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = ClientFor(_ => Json(HttpStatusCode.OK, ReleaseJson("v1.4.0")), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), cts.Token);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    // ---- url fallback ----

    [Fact]
    public async Task CheckAsync_MissingHtmlUrl_FallsBackToReleasesPage()
    {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, """{"tag_name":"v1.4.0"}"""), out _);

        var result = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("https://github.com/bitlamas/cc-usage-monitor/releases", result.ReleaseUrl);
    }
}
