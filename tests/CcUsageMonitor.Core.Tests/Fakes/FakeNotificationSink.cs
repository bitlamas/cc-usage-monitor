using System;
using System.Collections.Generic;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor.Core.Tests.Fakes;

/// <summary>
/// Fake INotificationSink for deterministic alert testing.
/// Captures all toasts and allows controlling availability.
/// </summary>
public class FakeNotificationSink : INotificationSink
{
    private readonly List<(string Title, string Body)> _showCalls = new();
    private bool _available = true;

    /// <summary>
    /// Whether the sink is currently available to show toasts.
    /// </summary>
    public bool Available
    {
        get => _available;
        set => _available = value;
    }

    /// <summary>
    /// All toast show calls made during the test.
    /// </summary>
    public IReadOnlyList<(string Title, string Body)> ShowCalls => _showCalls;

    /// <summary>
    /// Number of toast show calls.
    /// </summary>
    public int CallCount => _showCalls.Count;

    /// <summary>
    /// Clears all captured calls.
    /// </summary>
    public void Clear()
    {
        _showCalls.Clear();
    }

    /// <inheritdoc />
    public bool TryShow(string title, string body)
    {
        if (!_available)
            return false;

        _showCalls.Add((title, body));
        return true;
    }
}
