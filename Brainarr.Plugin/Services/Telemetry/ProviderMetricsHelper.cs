using System;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry
{
    public static class ProviderMetricsHelper
    {
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

        public static string BuildLatencyMetric(string provider, string model)
            => $"provider.latency.{SanitizeName(provider)}.{SanitizeName(model)}";

        public static string BuildErrorMetric(string provider, string model)
            => $"provider.errors.{SanitizeName(provider)}.{SanitizeName(model)}";

        public static string BuildThrottleMetric(string provider, string model)
            => $"provider.429.{SanitizeName(provider)}.{SanitizeName(model)}";
    }
}
