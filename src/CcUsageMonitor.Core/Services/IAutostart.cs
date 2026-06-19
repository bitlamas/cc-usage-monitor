namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Cross-platform launch-at-login interface.
/// Per spec §4.8 — mockable via the interface for tests.
/// </summary>
public interface IAutostart
{
    /// <summary>Whether autostart is currently enabled.</summary>
    bool IsEnabled();

    /// <summary>Enable autostart (register launch-at-login).</summary>
    void Enable();

    /// <summary>Disable autostart (unregister launch-at-login).</summary>
    void Disable();
}
