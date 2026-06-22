using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CcUsageMonitor.Core.Models;

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
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true   // human-readable config.json (used by "Open config file")
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
            return _saveFirstRunDefaults();

        try
        {
            var raw = JsonSerializer.Deserialize<JsonConfig>(File.ReadAllText(_configPath), JsonOptions);
            return Sanitize(raw ?? new JsonConfig());
        }
        catch
        {
            // Corrupt JSON → defaults
            return Sanitize(new JsonConfig());
        }
    }

    // Rename to avoid conflict with Save
    private AppConfig _saveFirstRunDefaults()
    {
        var defaults = Sanitize(new JsonConfig());
        Save(defaults);
        return defaults;
    }

    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(ToJson(config), JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    private static JsonConfig ToJson(AppConfig config)
    {
        var selectedLimitsNames = new List<string>(config.SelectedLimits.Count);
        foreach (var kind in config.SelectedLimits)
            selectedLimitsNames.Add(kind.ToString());

        var pollElement = System.Text.Json.JsonSerializer.SerializeToElement(config.PollIntervalSeconds);
        var colors = config.Colors != null ? new Dictionary<string, string>(config.Colors) : null;

        return new JsonConfig
        {
            SelectedLimits = selectedLimitsNames,
            ShowNumberInRing = config.ShowNumberInRing,
            AlertsEnabled = config.AlertsEnabled,
            AlertThreshold = config.AlertThreshold,
            WarnThreshold = config.WarnThreshold,
            PollIntervalSeconds = pollElement,
            StartAtLogin = config.StartAtLogin,
            Colors = colors
        };
    }

    private static AppConfig Sanitize(JsonConfig raw)
    {
        var selectedLimits = SanitizeSelectedLimits(raw.SelectedLimits);
        var showNumber = raw.ShowNumberInRing is true;
        var alertsEnabled = raw.AlertsEnabled != false;  // default true

        // Threshold validation: merge defaults first, then check the pair (spec §4.7)
        (var warn, var alert) = ValidateThresholds(raw.WarnThreshold, raw.AlertThreshold);

        var pollInterval = SanitizePollInterval(raw.PollIntervalSeconds);
        var startAtLogin = raw.StartAtLogin != false;  // default true
        var colors = SanitizeColors(raw.Colors);

        return new AppConfig(selectedLimits, showNumber, alertsEnabled, alert, warn, pollInterval, startAtLogin, colors);
    }

    private static List<LimitKind> SanitizeSelectedLimits(IReadOnlyList<string>? raw)
    {
        var defaults = new List<LimitKind> { LimitKind.Session5h, LimitKind.WeeklyAll };

        if (raw == null || raw.Count == 0)
            return defaults;

        var validNames = new HashSet<string> { "Session5h", "WeeklyAll", "WeeklySonnet", "WeeklyOpus" };
        var seen = new HashSet<string>();
        var result = new List<LimitKind>();

        foreach (var item in raw)
        {
            if (validNames.Contains(item) && seen.Add(item))
                result.Add((LimitKind)Enum.Parse(typeof(LimitKind), item));
        }

        return result.Count > 0 ? result : defaults;
    }

    private static (int warn, int alert) ValidateThresholds(int? rawWarn, int? rawAlert)
    {
        // Merge defaults first, THEN check the FULL spec condition.
        // Both fall back if the relationship OR range is violated (spec §4.7).
        int warn  = rawWarn  ?? 70;
        int alert = rawAlert ?? 90;
        if (!(0 <= warn && warn < alert && alert <= 100))
        {
            warn  = 70;
            alert = 90;
        }
        return (warn, alert);
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

    // Raw deserialized config (JSON types for deserialization)
    private class JsonConfig
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
