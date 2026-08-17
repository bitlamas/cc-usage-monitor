namespace CcUsageMonitor;

/// <summary>Static holder for the global log instance and config directory.</summary>
public static class Diagnostics
{
    public static Core.Services.ILog Log = new Core.Services.NullLog();
    public static string ConfigDir = "";
}
