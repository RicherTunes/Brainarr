using System;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Telemetry
{
    public class MetricsCollectorExporterTests
    {
        [Fact]
        public void Exporter_emits_single_HELP_TYPE_per_metric_and_sorted_labels()
        {
            var tagsA = new System.Collections.Generic.Dictionary<string, string>
            {
                { "provider", "openai" },
                { "model", "gpt-4o-mini" }
            };
            var tagsB = new System.Collections.Generic.Dictionary<string, string>
            {
                { "model", "gpt-4o" },
                { "provider", "openai" }
            };

            // Two label-sets with same keys in different order
            MetricsCollector.RecordTiming("provider.latency", TimeSpan.FromMilliseconds(10), tagsA);
            MetricsCollector.RecordTiming("provider.latency", TimeSpan.FromMilliseconds(20), tagsB);

            var text = MetricsCollector.ExportPrometheus();
            var lines = text.Split('\n');

            lines.Count(l => l.StartsWith("# HELP provider_latency_seconds ")).Should().Be(1);
            lines.Count(l => l.StartsWith("# TYPE provider_latency_seconds ")).Should().Be(1);

            // Labels sorted: model before provider? Our export sorts by key, so model then provider alphabetically
            lines.Any(l => l.Contains("provider_latency_seconds_avg{model=\"gpt-4o-mini\",provider=\"openai\"}")).Should().BeTrue();
        }
    }
}
