using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// JSON-backed config store. Dir injected for test temp dirs.
/// Per spec §4.7: raw deserialized → sanitized split; sanitization is the single chokepoint.
/// </summary>
public class ConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    // Default values per spec §4.7
    private const string DefaultGreen = "#4CAF50";
    private const string DefaultYellow = "#FFC107";
    private const string DefaultRed = "#F44336";
    private const string DefaultTrack = "#222222";
    private const string DefaultDim = "#757575";

    private readonly string _configPath;

    public ConfigStore(string configDir)
    {
        _configPath = Path.Combine(configDir, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
            return SaveFirstRunDefaults();

        try
        {
            var raw = JsonSerializer.Deserialize<RawConfig>(File.ReadAllText(_configPath), JsonOptions);
            return Sanitize(raw ?? new RawConfig());
        }
        catch
        {
            // Corrupt JSON → defaults
            return Sanitize(new RawConfig());
        }
    }

    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    private AppConfig SaveFirstRunDefaults()
    {
        var defaults = Sanitize(new RawConfig());
        Save(defaults);
        return defaults;
    }

    private static AppConfig Sanitize(RawConfig raw)
    {
        var selectedLimits = SanitizeSelectedLimits(raw.SelectedLimits);
        var showNumber = raw.ShowNumberInRing is true;
        var alertsEnabled = raw.AlertsEnabled != false;  // default true

        // Threshold validation on the FINAL merged pair
        var warn = SanitizeWarnThreshold(raw.WarnThreshold);
        var alert = SanitizeAlertThreshold(raw.AlertThreshold);
        ValidateThresholds(ref warn, ref alert);

        var pollInterval = SanitizePollInterval(raw.PollIntervalSeconds);
        var startAtLogin = raw.StartAtLogin != false;  // default true
        var colors = SanitizeColors(raw.Colors);

        return new AppConfig(selectedLimits, showNumber, alertsEnabled, alert, warn, pollInterval, startAtLogin, colors);
    }

    private static List<string> SanitizeSelectedLimits(IReadOnlyList<string>? raw)
    {
        if (raw == null || raw.Count == 0)
            return new List<string> { "Session5h", "WeeklyAll" };

        var valid = new HashSet<string> { "Session5h", "WeeklyAll", "WeeklySonnet", "WeeklyOpus" };
        var seen = new HashSet<string>();
        var result = new List<string>();

        foreach (var item in raw)
        {
            if (valid.Contains(item) && seen.Add(item))
                result.Add(item);
        }

        return result.Count > 0 ? result : new List<string> { "Session5h", "WeeklyAll" };
    }

    private static int SanitizeWarnThreshold(int? raw)
    {
        if (raw is >= 0 and <= 100)
            return raw.Value;
        return 70; // default
    }

    private static int SanitizeAlertThreshold(int? raw)
    {
        if (raw is >= 0 and <= 100)
            return raw.Value;
        return 90; // default
    }

    private static void ValidateThresholds(ref int warn, ref int alert)
    {
        // Enforce 0 <= warn < alert <= 100 on the final merged pair
        if (warn >= alert || warn < 0 || alert > 100)
        {
            warn = 70;
            alert = 90;
        }
    }

    private static int SanitizePollInterval(JsonElement? raw)
    {
        if (raw.HasValue && raw.Value.ValueKind == JsonValueKind.Number)
        {
            if (raw.Value.TryGetInt64(out var l))
                return Math.Max(30, Math.Min(3600, (int)l));
        }
        // Non-integer → default 180
        return 180;
    }

    private static Dictionary<string, string> SanitizeColors(Dictionary<string, string>? raw)
    {
        var defaults = new Dictionary<string, string>
        {
            { "green", DefaultGreen },
            { "yellow", DefaultYellow },
            { "red", DefaultRed },
            { "track", DefaultTrack },
            { "dim", DefaultDim }
        };

        if (raw == null)
            return defaults;

        var result = new Dictionary<string, string>(defaults);
        var hexPattern = @"^#[0-9A-Fa-f]{6}$";

        foreach (var kvp in raw)
        {
            if (defaults.ContainsKey(kvp.Key) && System.Text.RegularExpressions.Regex.IsMatch(kvp.Value, hexPattern))
                result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    // Raw deserialized config (pre-sanitization)
    private class RawConfig
    {
        [JsonPropertyName("selectedLimits")]
        public IReadOnlyList<string>? SelectedLimits { get; set; }

        [JsonPropertyName("showNumberInRing")]
        public bool? ShowNumberInRing { get; set; }

        [JsonPropertyName("alertsEnabled")]
        public bool? AlertsEnabled { get; set; }

        [JsonPropertyName("alertThreshold")]
        public int? AlertThreshold { get; set; }

        [JsonPropertyName("warnThreshold")]
        public int? WarnThreshold { get; set; }

        [JsonPropertyName("pollIntervalSeconds")]
        public JsonElement? PollIntervalSeconds { get; set; }

        [JsonPropertyName("startAtLogin")]
        public bool? StartAtLogin { get; set; }

        [JsonPropertyName("colors")]
        public Dictionary<string, string>? Colors { get; set; }
    }
}
