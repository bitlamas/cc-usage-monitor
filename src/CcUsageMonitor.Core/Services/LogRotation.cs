namespace CcUsageMonitor.Core.Services;

/// <summary>Pure log-rotation helpers.</summary>
public static class LogRotation
{
    /// <summary>True when the existing log should be rolled BEFORE the next write. Boundary is inclusive.</summary>
    public static bool ShouldRoll(long currentSizeBytes, long maxSizeBytes)
        => currentSizeBytes >= maxSizeBytes;
}
