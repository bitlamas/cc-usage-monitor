using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using CcUsageMonitor.Core.Tests.Fakes;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class UsageClientClassificationTests
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public UsageClientClassificationTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    #region Helpers

    private UsageClient MakeClient(FakeHttpHandler handler, string? fixturePath = null)
    {
        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0);
        var store = new CredentialStore(fakeRunner, fixturePath ?? SetupCredentials());
        return new UsageClient(store, "api-usage-2026-05-29", new HttpClient(handler));
    }

    private string SetupCredentials(string? dir = null)
    {
        var d = dir ?? _tempDir;
        var fixturePath = Path.Combine(d, ".credentials.json");
        var fixtureContent = File.ReadAllText(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials.json"));
        File.WriteAllText(fixturePath, fixtureContent);
        return fixturePath;
    }

    #endregion

    #region A6.x acceptance tests

    [Fact]
    public async Task Fetch_429WithRetryAfter300_ClassifiesRateLimited() // A6.1
    {
        var handler = new FakeHttpHandler();
        var resp = new HttpResponseMessage((HttpStatusCode)429) { ReasonPhrase = "Too Many Requests" };
        resp.Headers.TryAddWithoutValidation("Retry-After", "300");
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage", resp);

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.RateLimited, snap.ErrorKind);
        Assert.Equal(300, snap.RetryAfterSeconds);
        Assert.Equal("HTTP 429 Too Many Requests", snap.Error);
        Assert.True(snap.Stale);
    }

    [Fact]
    public async Task Fetch_429WithRetryAfterZero_YieldsZeroNotNull() // A6.2
    {
        var handler = new FakeHttpHandler();
        var resp = new HttpResponseMessage((HttpStatusCode)429) { ReasonPhrase = "Too Many Requests" };
        resp.Headers.TryAddWithoutValidation("Retry-After", "0");
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage", resp);

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.RateLimited, snap.ErrorKind);
        Assert.Equal(0, snap.RetryAfterSeconds);
    }

    [Fact]
    public async Task Fetch_429NoRetryAfterHeader_YieldsNullRetryAfter() // A6.3
    {
        var handler = new FakeHttpHandler();
        var resp = new HttpResponseMessage((HttpStatusCode)429) { ReasonPhrase = "Too Many Requests" };
        // No Retry-After header
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage", resp);

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.RateLimited, snap.ErrorKind);
        Assert.Null(snap.RetryAfterSeconds);
    }

    [Fact]
    public async Task Fetch_503_ClassifiesServerError() // A6.4
    {
        var handler = new FakeHttpHandler();
        var resp = new HttpResponseMessage((HttpStatusCode)503) { ReasonPhrase = "Service Unavailable" };
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage", resp);

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.ServerError, snap.ErrorKind);
        Assert.Null(snap.RetryAfterSeconds);
    }

    [Fact]
    public async Task Fetch_NoToken_ClassifiesNotLoggedIn() // A6.5
    {
        var fakeRunner = new FakeProcessRunner();
        var store = new CredentialStore(fakeRunner, Path.Combine(_tempDir, "nonexistent.json"));
        var handler = new FakeHttpHandler();
        var client = new UsageClient(store, "api-usage-2026-05-29", new HttpClient(handler));

        var snap = await client.FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.NotLoggedIn, snap.ErrorKind);
    }

    [Fact]
    public async Task Fetch_200Success_YieldsNullErrorKind() // A6.6
    {
        var handler = new FakeHttpHandler();
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""five_hour"":{""utilization"":50.0},""seven_day"":{""utilization"":30.0},""seven_day_sonnet"":null,""seven_day_opus"":{""utilization"":10.0}}")
            });

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Null(snap.ErrorKind);
        Assert.Null(snap.RetryAfterSeconds);
        Assert.False(snap.Stale);
        Assert.Null(snap.Error);
    }

    [Fact]
    public async Task Fetch_Malformed200Body_ClassifiesServerError() // A6.7
    {
        var handler = new FakeHttpHandler();
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not valid json {{{")
            });

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.ServerError, snap.ErrorKind);
        Assert.Equal("Failed to parse usage response", snap.Error);
    }

    [Fact]
    public async Task Fetch_TransportThrow_ClassifiesNetworkError() // A6.8
    {
        var handler = new FakeHttpHandler();
        handler.SetThrow(new HttpRequestException("network unreachable"));
        handler.SetCaptureRequests(false);

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.NetworkError, snap.ErrorKind);
        Assert.Null(snap.RetryAfterSeconds);
        Assert.True(snap.Stale);
    }

    [Fact]
    public async Task Fetch_429WithHTTPDateRetryAfter_YieldsNullRetryAfter() // A6.9
    {
        var handler = new FakeHttpHandler();
        var resp = new HttpResponseMessage((HttpStatusCode)429) { ReasonPhrase = "Too Many Requests" };
        // HTTP-date form — should be treated as absent
        resp.Headers.TryAddWithoutValidation("Retry-After", "Wed, 21 Oct 2026 07:28:00 GMT");
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage", resp);

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.RateLimited, snap.ErrorKind);
        Assert.Null(snap.RetryAfterSeconds);
    }

    [Fact]
    public async Task Fetch_429WithNonIntegerRetryAfter_YieldsNullRetryAfter() // A6.10
    {
        var handler = new FakeHttpHandler();
        var resp = new HttpResponseMessage((HttpStatusCode)429) { ReasonPhrase = "Too Many Requests" };
        // Non-integer Retry-After — must be set via raw header to avoid DeltaSeconds parser
        resp.Headers.TryAddWithoutValidation("Retry-After", "abc");
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage", resp);

        var snap = await MakeClient(handler).FetchAsync(CancellationToken.None);

        Assert.Equal(ErrorKind.RateLimited, snap.ErrorKind);
        Assert.Null(snap.RetryAfterSeconds);
    }

    #endregion
}
