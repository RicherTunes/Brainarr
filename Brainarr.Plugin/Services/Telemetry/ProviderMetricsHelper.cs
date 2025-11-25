using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry
{
    public static class ProviderMetricsHelper
    {
        // Metric names (flat, label-based)
        public const string ProviderLatencyMs = "provider.latency"; // exported as provider_latency_seconds_*
        public const string ProviderErrorsTotal = "provider.errors"; // exported as provider_errors_total
        public const string ProviderThrottlesTotal = "provider.429"; // exported as provider_throttles_total (internal key retains 429 for back-compat)

        public static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            var v = value.Trim().ToLowerInvariant();
            // replace chars Prometheus/our aggregator might dislike
            foreach (var ch in new[] { ' ', '/', '\\', ':', '.', '|', '#', '?', '&', '=', '(', ')', '[', ']', '{', '}', ',' })
            {
                v = v.Replace(ch, '-');
            }
            while (v.Contains("--")) v = v.Replace("--", "-");
            return v.Trim('-');
        }

        public static IReadOnlyDictionary<string, string> BuildTags(string provider, string model)
            => new Dictionary<string, string>
            {
                ["provider"] = SanitizeName(provider),
                ["model"] = SanitizeName(model)
            };
    }
}
