using System.IO;

namespace CcUsageMonitor.Core.Services;

/// <summary>Pure log-file path helpers.</summary>
public static class LogPaths
{
    public static string LogFile(string configDir)
        => Path.Combine(configDir, "logs", "cc-usage-monitor.log");

    public static string RolledFile(string configDir)
        => Path.Combine(configDir, "logs", "cc-usage-monitor.log.1");
}
