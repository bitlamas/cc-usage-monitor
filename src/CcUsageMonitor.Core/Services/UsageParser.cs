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
        "seven_day",
        "seven_day_sonnet",
        "seven_day_opus"
    };

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

            var present = true;

            // Check utilization
            int? pct = null;
            if (bucket.TryGetProperty("utilization", out var utilProp) && utilProp.ValueKind == JsonValueKind.Number)
            {
                // Round-half-up (AwayFromZero), not banker's rounding
                pct = (int)Math.Round(utilProp.GetDouble(), MidpointRounding.AwayFromZero);
            }

            // Check resets_at
            DateTimeOffset? resetsAt = null;
            if (bucket.TryGetProperty("resets_at", out var resetsProp) && resetsProp.ValueKind == JsonValueKind.String)
            {
                var resetsStr = resetsProp.GetString();
                if (resetsStr != null && DateTimeOffset.TryParse(resetsStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    resetsAt = parsed;
            }

            limits[kind] = new LimitState(pct, resetsAt, present);
        }

        return new UsageSnapshot(
            new Dictionary<LimitKind, LimitState>(limits),
            DateTimeOffset.UtcNow,
            Error: null,
            Stale: false);
    }
}
