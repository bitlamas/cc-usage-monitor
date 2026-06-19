namespace CcUsageMonitor.Core.Services;

/// <summary>Real-world clock implementing IClock.</summary>
public class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
