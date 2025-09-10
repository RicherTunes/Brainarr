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
            MetricsCollector.RecordTiming("provider.latency.openai.gpt-4o-mini", TimeSpan.FromMilliseconds(123));
            MetricsCollector.IncrementCounter("provider.errors.openai.gpt-4o-mini");

            var text = MetricsCollector.ExportPrometheus();
            text.Should().Contain("provider_latency_openai_gpt-4o-mini_p95");
            text.Should().Contain("provider_errors_openai_gpt-4o-mini_count");
        }
    }
}
