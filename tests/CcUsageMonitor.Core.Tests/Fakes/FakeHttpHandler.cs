using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CcUsageMonitor.Core.Tests.Fakes;

/// <summary>
/// Fake HTTP handler for deterministic testing of UsageClient.
/// Allows configuring responses and capturing request details (headers, methods).
/// </summary>
public class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<(string method, string url), HttpResponseMessage> _responses = new();
    private readonly List<HttpRequestMessage> _requests = new();
    private HttpResponseMessage? _defaultResponse;
    private bool _captureRequests = true;

    /// <summary>Set a response for a specific method+URL.</summary>
    public void SetResponse(string method, string url, HttpResponseMessage response)
    {
        _responses[(method, url)] = response;
    }

    /// <summary>Set a default response for unmatched requests.</summary>
    public void SetDefaultResponse(HttpResponseMessage response)
    {
        _defaultResponse = response;
    }

    /// <summary>Whether to capture requests for inspection.</summary>
    public void SetCaptureRequests(bool value)
    {
        _captureRequests = value;
    }

    /// <summary>All requests made during the test session.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    /// <summary>Clear all captured requests.</summary>
    public void ClearRequests()
    {
        _requests.Clear();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_captureRequests)
            _requests.Add(request);

        var key = (request.Method.Method, request.RequestUri?.ToString() ?? string.Empty);

        if (_responses.TryGetValue(key, out var response))
            return Task.FromResult(response);

        if (_defaultResponse is not null)
            return Task.FromResult(_defaultResponse);

        return Task.FromResult<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
