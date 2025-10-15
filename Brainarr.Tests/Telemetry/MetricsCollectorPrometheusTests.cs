using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Telemetry
{
    public class MetricsCollectorPrometheusTests
    {
        [Fact]
        public void ExportPrometheus_includes_expected_lines()
        {
            var tags = new System.Collections.Generic.Dictionary<string, string>
            {
                { "provider", "openai" },
                { "model", "gpt-4o-mini" }
            };
            MetricsCollector.RecordTiming("provider.latency", TimeSpan.FromMilliseconds(123), tags);
            MetricsCollector.IncrementCounter("provider.errors", tags);

            var text = MetricsCollector.ExportPrometheus();
            // Timing emits metrics with labels
            text.Should().Contain("provider_latency_ms_p95{provider=\"openai\",model=\"gpt-4o-mini\"}");
            // Error counter should have a labeled total line
            text.Should().Contain("provider_errors_total{provider=\"openai\",model=\"gpt-4o-mini\"}");
        }
    }
}
