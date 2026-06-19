using System.Collections.Generic;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor.Core.Tests.Fakes;

/// <summary>
/// Fake IAutostart for deterministic testing.
/// Simulates enabled state via a boolean and records Enable/Disable calls.
/// </summary>
public class FakeAutostart : IAutostart
{
    private bool _enabled = false;

    /// <summary>Whether autostart is considered enabled.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>All Enable calls made during the test.</summary>
    public int EnableCallCount { get; private set; }

    /// <summary>All Disable calls made during the test.</summary>
    public int DisableCallCount { get; private set; }

    /// <summary>Clears state and call counters.</summary>
    public void Clear()
    {
        _enabled = false;
        EnableCallCount = 0;
        DisableCallCount = 0;
    }

    public bool IsEnabled()
        => _enabled;

    public void Enable()
    {
        _enabled = true;
        EnableCallCount++;
    }

    public void Disable()
    {
        _enabled = false;
        DisableCallCount++;
    }
}
