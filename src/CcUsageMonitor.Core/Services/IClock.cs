namespace CcUsageMonitor.Core.Services;

/// <summary>Current-time seam — injected for deterministic formatting tests.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}
