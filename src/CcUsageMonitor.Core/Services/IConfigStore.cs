namespace CcUsageMonitor.Core.Services;

/// <summary>Persisted application configuration.</summary>
public record AppConfig(
    IReadOnlyList<string> SelectedLimits,
    bool ShowNumberInRing,
    bool AlertsEnabled,
    int AlertThreshold,
    int WarnThreshold,
    int PollIntervalSeconds,
    bool StartAtLogin,
    IReadOnlyDictionary<string, string> Colors);

/// <summary>Config store — load, save, validate.</summary>
public interface IConfigStore
{
    AppConfig Load();
    void Save(AppConfig config);
}
