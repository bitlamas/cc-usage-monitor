using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using CcUsageMonitor.Core.Tests.Fakes;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class UsageClientTests
{
    private readonly string _tempDir;

    public UsageClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    #region HTTP headers

    [Fact]
    public async Task UsageClient_FetchAsync_NoContentTypeHeader()
    {
        // Arrange
        var fixturePath = SetupCredentials();
        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0);
        var store = new CredentialStore(fakeRunner, fixturePath);

        var handler = new FakeHttpHandler();
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""five_hour"":{""utilization"":50.0},""seven_day"":{""utilization"":30.0},""seven_day_sonnet"":null,""seven_day_opus"":{""utilization"":10.0}}")
            });

        using var httpClient = new HttpClient(handler);
        var client = new UsageClient(store, "api-usage-2026-05-29", httpClient);

        // Act
        await client.FetchAsync(CancellationToken.None);

        // Assert — no Content-Type header on the outgoing request
        var request = handler.Requests[0];
        // Check all header names — Content-Type must not be present
        var hasContentType = request.Headers.Any(h =>
            string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase));
        Assert.False(hasContentType,
            "Bodyless GET must NOT carry a Content-Type header (spec §4.2 trap)");
    }

    [Fact]
    public async Task UsageClient_FetchAsync_UserAgentMatchesPattern()
    {
        // Arrange
        var fixturePath = SetupCredentials();
        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0);
        var store = new CredentialStore(fakeRunner, fixturePath);

        var handler = new FakeHttpHandler();
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""five_hour"":{""utilization"":50.0},""seven_day"":{""utilization"":30.0},""seven_day_sonnet"":null,""seven_day_opus"":{""utilization"":10.0}}")
            });

        using var httpClient = new HttpClient(handler);
        var client = new UsageClient(store, "api-usage-2026-05-29", httpClient);

        // Act
        await client.FetchAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests[0];
        Assert.True(request.Headers.TryGetValues("User-Agent", out var uaValues));
        var ua = string.Join(", ", uaValues);
        Assert.Matches(@"^cc-usage-monitor/\S+$", ua);
    }

    #endregion

    #region 401 refresh-retry

    [Fact]
    public async Task UsageClient_FetchAsync_401_RetriesOnce()
    {
        // Arrange
        var fixturePath = SetupCredentials();
        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0);
        var store = new CredentialStore(fakeRunner, fixturePath);

        var handler = new FakeHttpHandler();
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage",
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        // After refresh, second request also 401
        handler.SetDefaultResponse(
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var httpClient = new HttpClient(handler);
        var client = new UsageClient(store, "api-usage-2026-05-29", httpClient);

        // Act
        var snapshot = await client.FetchAsync(CancellationToken.None);

        // Assert — two requests made (original + retry)
        Assert.Equal(2, handler.Requests.Count);

        // Error set, stale, last-good retained (empty snapshot since no prior good data)
        Assert.NotNull(snapshot.Error);
        Assert.True(snapshot.Stale);
    }

    [Fact]
    public async Task UsageClient_FetchAsync_401AfterRetry_ErrorNoToken()
    {
        // Arrange
        var fixturePath = SetupCredentials();
        var fakeRunner = new FakeProcessRunner();
        fakeRunner.SetExitCode(0);
        var store = new CredentialStore(fakeRunner, fixturePath);

        var handler = new FakeHttpHandler();
        handler.SetResponse("GET", "https://api.anthropic.com/api/oauth/usage",
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.SetDefaultResponse(
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var httpClient = new HttpClient(handler);
        var client = new UsageClient(store, "api-usage-2026-05-29", httpClient);

        // Act
        var snapshot = await client.FetchAsync(CancellationToken.None);

        // Assert — Error must never contain the token
        Assert.NotNull(snapshot.Error);
        Assert.DoesNotContain("sk-ant-example-token", snapshot.Error);
    }

    #endregion

    #region Error states

    [Fact]
    public async Task UsageClient_FetchAsync_NoToken_ReturnsError()
    {
        // Arrange
        var fakeRunner = new FakeProcessRunner();
        var store = new CredentialStore(fakeRunner, Path.Combine(_tempDir, "nonexistent.json"));

        var handler = new FakeHttpHandler();
        using var httpClient = new HttpClient(handler);
        var client = new UsageClient(store, "api-usage-2026-05-29", httpClient);

        // Act
        var snapshot = await client.FetchAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Not logged in to Claude Code", snapshot.Error);
        Assert.True(snapshot.Stale);
    }

    #endregion

    #region Helper

    private string SetupCredentials()
    {
        var fixturePath = Path.Combine(_tempDir, ".credentials.json");
        var fixtureContent = File.ReadAllText(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Fixtures", "credentials.json"));
        File.WriteAllText(fixturePath, fixtureContent);
        return fixturePath;
    }
    #endregion
}
