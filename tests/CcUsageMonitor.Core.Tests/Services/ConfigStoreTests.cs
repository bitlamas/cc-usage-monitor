using System;
using System.Collections.Generic;
using System.IO;
using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;
using Xunit;

namespace CcUsageMonitor.Core.Tests.Services;

public class ConfigStoreTests
{
    private string _tempDir;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ConfigStore_FirstRun_WritesDefaults()
    {
        var store = new ConfigStore(_tempDir);
        var config = store.Load();

        Assert.Contains(LimitKind.Session5h, config.SelectedLimits);
        Assert.Contains(LimitKind.WeeklyAll, config.SelectedLimits);
        Assert.False(config.ShowNumberInRing);
        Assert.True(config.AlertsEnabled);
        Assert.Equal(90, config.AlertThreshold);
        Assert.Equal(70, config.WarnThreshold);
        Assert.Equal(180, config.PollIntervalSeconds);
        Assert.True(config.StartAtLogin);

        // Verify defaults file was written
        var configPath = Path.Combine(_tempDir, "config.json");
        Assert.True(File.Exists(configPath));
    }

    [Fact]
    public void ConfigStore_RoundTrip()
    {
        var store = new ConfigStore(_tempDir);
        var original = new AppConfig(
            new List<LimitKind> { LimitKind.Session5h, LimitKind.WeeklySonnet },
            true,
            false,
            85,
            65,
            300,
            false,
            new Dictionary<string, string> { { "green", "#00FF00" } });

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.SelectedLimits, loaded.SelectedLimits);
        Assert.Equal(original.ShowNumberInRing, loaded.ShowNumberInRing);
        Assert.Equal(original.AlertsEnabled, loaded.AlertsEnabled);
        Assert.Equal(original.AlertThreshold, loaded.AlertThreshold);
        Assert.Equal(original.WarnThreshold, loaded.WarnThreshold);
        Assert.Equal(original.PollIntervalSeconds, loaded.PollIntervalSeconds);
        Assert.Equal(original.StartAtLogin, loaded.StartAtLogin);
        // Colors: SanitizeColors fills in defaults for missing keys
        Assert.Equal("#00FF00", loaded.Colors["green"]);
        Assert.Equal("#FFC107", loaded.Colors["yellow"]); // default
        Assert.Equal("#F44336", loaded.Colors["red"]); // default
        Assert.Equal("#222222", loaded.Colors["track"]); // default
        Assert.Equal("#757575", loaded.Colors["dim"]); // default
    }

    // pollIntervalSeconds validation
    [Theory]
    [InlineData(0, 30)]
    [InlineData(5, 30)]
    [InlineData(180, 180)]
    [InlineData(3600, 3600)]
    [InlineData(7200, 3600)]
    public void ConfigStore_PollIntervalClamped(int raw, int expected)
    {
        var json = $"{{\"pollIntervalSeconds\": {raw}}}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(expected, config.PollIntervalSeconds);
    }

    [Fact]
    public void ConfigStore_PollIntervalNonInteger_Defaults()
    {
        var json = "{\"pollIntervalSeconds\": 45.7}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(180, config.PollIntervalSeconds);
    }

    [Fact]
    public void ConfigStore_PollIntervalString_Defaults()
    {
        var json = "{\"pollIntervalSeconds\": \"abc\"}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(180, config.PollIntervalSeconds);
    }

    [Fact]
    public void ConfigStore_PollIntervalAbsent_Defaults()
    {
        var json = "{}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(180, config.PollIntervalSeconds);
    }

    // Threshold validation on all presence combos
    [Fact]
    public void ConfigStore_ThresholdsBothValid_Used()
    {
        var json = "{\"warnThreshold\": 50, \"alertThreshold\": 80}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(50, config.WarnThreshold);
        Assert.Equal(80, config.AlertThreshold);
    }

    [Fact]
    public void ConfigStore_ThresholdsInverted_FallsBack()
    {
        var json = "{\"warnThreshold\": 95, \"alertThreshold\": 80}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(70, config.WarnThreshold);
        Assert.Equal(90, config.AlertThreshold);
    }

    [Fact]
    public void ConfigStore_AlertOnly_Inverted_MergesToInvalid_FallsBack()
    {
        var json = "{\"alertThreshold\": 50}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        // alert=50 merges with warn default=70 → 70 >= 50 → invalid → both fallback
        Assert.Equal(70, config.WarnThreshold);
        Assert.Equal(90, config.AlertThreshold);
    }

    [Fact]
    public void ConfigStore_WarnOnly_Valid()
    {
        var json = "{\"warnThreshold\": 50}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(50, config.WarnThreshold);
        Assert.Equal(90, config.AlertThreshold); // alert stays at default
    }

    [Fact]
    public void ConfigStore_ThresholdNegative_FallsBack()
    {
        var json = "{\"warnThreshold\": -1, \"alertThreshold\": 90}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(70, config.WarnThreshold);
        Assert.Equal(90, config.AlertThreshold);
    }

    [Fact]
    public void ConfigStore_ThresholdOver100_FallsBack()
    {
        var json = "{\"warnThreshold\": 70, \"alertThreshold\": 101}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(70, config.WarnThreshold);
        Assert.Equal(90, config.AlertThreshold);
    }

    [Fact]
    public void ConfigStore_ThresholdAlertOver100_FallsBack()
    {
        var json = "{\"warnThreshold\": 50, \"alertThreshold\": 200}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(70, config.WarnThreshold);
        Assert.Equal(90, config.AlertThreshold);
    }

    [Fact]
    public void ConfigStore_ThresholdWarnNegative_FallsBack()
    {
        var json = "{\"warnThreshold\": -5, \"alertThreshold\": 80}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(70, config.WarnThreshold);
        Assert.Equal(90, config.AlertThreshold);
    }

    // selectedLimits validation
    [Fact]
    public void ConfigStore_SelectedLimitsUnknownDropped()
    {
        var json = "{\"selectedLimits\": [\"Session5h\", \"NonExistent\"]}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Single(config.SelectedLimits, k => k == LimitKind.Session5h);
    }

    [Fact]
    public void ConfigStore_SelectedLimitsDuplicatesDeduped()
    {
        var json = "{\"selectedLimits\": [\"Session5h\", \"Session5h\", \"WeeklyAll\"]}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Single(config.SelectedLimits, LimitKind.Session5h);
        Assert.Single(config.SelectedLimits, LimitKind.WeeklyAll);
        Assert.Equal(2, config.SelectedLimits.Count);
    }

    [Fact]
    public void ConfigStore_SelectedLimitsAllUnknown_FallsBack()
    {
        var json = "{\"selectedLimits\": [\"NonExistent\", \"AlsoUnknown\"]}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Contains(LimitKind.Session5h, config.SelectedLimits);
        Assert.Contains(LimitKind.WeeklyAll, config.SelectedLimits);
    }

    [Fact]
    public void ConfigStore_SelectedLimitsOnlyNotPresentKind_Kept()
    {
        var json = "{\"selectedLimits\": [\"WeeklyOpus\"]}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Single(config.SelectedLimits, LimitKind.WeeklyOpus);
    }

    // Colors validation
    [Fact]
    public void ConfigStore_ColorsPartial_OverridesApplied()
    {
        var json = "{\"colors\": {\"green\": \"#FF0000\"}}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal("#FF0000", config.Colors["green"]);
        Assert.Equal("#FFC107", config.Colors["yellow"]); // default
    }

    [Fact]
    public void ConfigStore_ColorsEmpty_Valid()
    {
        var json = "{\"colors\": {}}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal("#4CAF50", config.Colors["green"]);
        Assert.Equal("#FFC107", config.Colors["yellow"]);
        Assert.Equal("#F44336", config.Colors["red"]);
        Assert.Equal("#222222", config.Colors["track"]);
        Assert.Equal("#757575", config.Colors["dim"]);
    }

    [Fact]
    public void ConfigStore_ColorsInvalidKey_Dropped()
    {
        var json = "{\"colors\": {\"green\": \"not-a-color\", \"yellow\": \"#00FF00\"}}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal("#4CAF50", config.Colors["green"]); // invalid → default
        Assert.Equal("#00FF00", config.Colors["yellow"]); // valid override
    }

    // Corrupt JSON
    [Fact]
    public void ConfigStore_CorruptJson_Defaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "not valid json{{{");
        var config = new ConfigStore(_tempDir).Load();
        Assert.Contains(LimitKind.Session5h, config.SelectedLimits);
        Assert.Equal(180, config.PollIntervalSeconds);
        Assert.Equal(70, config.WarnThreshold);
        Assert.Equal(90, config.AlertThreshold);
    }

    // Unknown keys ignored (forward compat)
    [Fact]
    public void ConfigStore_UnknownKeys_Ignored()
    {
        var json = "{\"unknownKey\": true, \"alsoUnknown\": 42}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Equal(180, config.PollIntervalSeconds); // default still used
    }

    // Forward-compat: null/missing boolean fields
    [Fact]
    public void ConfigStore_BoolFieldsAbsent_Defaults()
    {
        var json = "{\"pollIntervalSeconds\": 180}";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.False(config.ShowNumberInRing);
        Assert.True(config.AlertsEnabled);
        Assert.True(config.StartAtLogin);
    }

    [Fact]
    public void ConfigStore_AllFieldsPresent_RoundTrip()
    {
        var json = @"{
            ""selectedLimits"": [""Session5h"", ""WeeklyAll"", ""WeeklySonnet""],
            ""showNumberInRing"": true,
            ""alertsEnabled"": true,
            ""alertThreshold"": 85,
            ""warnThreshold"": 65,
            ""pollIntervalSeconds"": 300,
            ""startAtLogin"": false,
            ""colors"": { ""green"": ""#00AA00"", ""dim"": ""#888888"" }
        }";
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);
        var config = new ConfigStore(_tempDir).Load();
        Assert.Single(config.SelectedLimits, LimitKind.Session5h);
        Assert.Single(config.SelectedLimits, LimitKind.WeeklyAll);
        Assert.Single(config.SelectedLimits, LimitKind.WeeklySonnet);
        Assert.Equal(3, config.SelectedLimits.Count);
        Assert.True(config.ShowNumberInRing);
        Assert.True(config.AlertsEnabled);
        Assert.Equal(85, config.AlertThreshold);
        Assert.Equal(65, config.WarnThreshold);
        Assert.Equal(300, config.PollIntervalSeconds);
        Assert.False(config.StartAtLogin);
        Assert.Equal("#00AA00", config.Colors["green"]);
        Assert.Equal("#888888", config.Colors["dim"]);
    }
}
