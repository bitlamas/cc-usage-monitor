using System;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor.Core.Tests.Fakes;

/// <summary>
/// Fake IClock for deterministic testing.
/// </summary>
public class FakeClock : IClock
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset? now = null)
    {
        _now = now ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset Now => _now;

    /// <summary>
    /// Sets the clock to a specific value.
    /// </summary>
    public void Set(DateTimeOffset value)
    {
        _now = value;
    }

    /// <summary>
    /// Advances the clock by the specified TimeSpan.
    /// </summary>
    public void Advance(TimeSpan amount)
    {
        _now = _now + amount;
    }
}
