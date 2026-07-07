using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

/// <summary>
/// Pure JSON parser for the Anthropic usage API response.
/// Per spec §4.2: parse buckets, round-half-up, null handling.
/// </summary>
public static class UsageParser
{
    private static readonly string[] TargetBuckets =
    {
        "five_hour",
        "seven_day"
    };

    // The scoped weekly limit we surface as WeeklyFable is identified in the API's
    // limits[] array by kind + model display name (case-sensitive, matches the API).
    private const string ScopedWeeklyKind = "weekly_scoped";
    private const string FableModelDisplayName = "Fable";

    /// <summary>
    /// Parses the JSON response body into a UsageSnapshot.
    /// </summary>
    /// <param name="json">The raw JSON response body.</param>
    /// <returns>A UsageSnapshot with per-limit LimitState entries.</returns>
    public static UsageSnapshot Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;

        var limits = new Dictionary<LimitKind, LimitState>();

        foreach (var bucketName in TargetBuckets)
        {
            // All TargetBuckets have known mappings, so this is never null
            var kind = LimitKindMapping.GetKindFromBucketKey(bucketName)!.Value;

            if (!root.TryGetProperty(bucketName, out var bucket) || bucket.ValueKind == JsonValueKind.Null)
            {
                limits[kind] = new LimitState(null, null, Present: false);
                continue;
            }

            // Top-level buckets carry the percentage under "utilization".
            var pct = ReadPercent(bucket, "utilization");
            var resetsAt = ReadResetsAt(bucket);
            limits[kind] = new LimitState(pct, resetsAt, Present: true);
        }

        // WeeklyFable is not a top-level bucket: it lives in the limits[] array as a
        // weekly_scoped entry scoped to the "Fable" model. Absent → Present: false.
        limits[LimitKind.WeeklyFable] = ParseFable(root);

        return new UsageSnapshot(
            new Dictionary<LimitKind, LimitState>(limits),
            DateTimeOffset.UtcNow,
            Error: null,
            Stale: false);
    }

    /// <summary>
    /// Finds the Fable weekly limit in the limits[] array. Returns an absent LimitState
    /// (Present: false) when the array, the weekly_scoped/Fable entry, or the scope is missing.
    /// </summary>
    private static LimitState ParseFable(JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var limitsArr) || limitsArr.ValueKind != JsonValueKind.Array)
            return new LimitState(null, null, Present: false);

        foreach (var entry in limitsArr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            if (!entry.TryGetProperty("kind", out var kindProp) ||
                kindProp.ValueKind != JsonValueKind.String ||
                kindProp.GetString() != ScopedWeeklyKind)
                continue;

            if (!entry.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object ||
                !scope.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object ||
                !model.TryGetProperty("display_name", out var name) || name.ValueKind != JsonValueKind.String ||
                name.GetString() != FableModelDisplayName)
                continue;

            // Scoped limits carry the percentage under "percent" (already 0–100).
            var pct = ReadPercent(entry, "percent");
            var resetsAt = ReadResetsAt(entry);
            return new LimitState(pct, resetsAt, Present: true);
        }

        return new LimitState(null, null, Present: false);
    }

    /// <summary>Reads a numeric percentage field, round-half-up (AwayFromZero). Null when absent/non-numeric.</summary>
    private static int? ReadPercent(JsonElement obj, string property)
    {
        if (obj.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return (int)Math.Round(prop.GetDouble(), MidpointRounding.AwayFromZero);
        return null;
    }

    /// <summary>Reads and parses the "resets_at" ISO-8601 string. Null when absent/null/unparseable.</summary>
    private static DateTimeOffset? ReadResetsAt(JsonElement obj)
    {
        if (obj.TryGetProperty("resets_at", out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var s = prop.GetString();
            if (s != null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
        }
        return null;
    }
}
