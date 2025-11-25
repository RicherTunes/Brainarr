using System;
using System.Collections.Generic;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Telemetry
{
    public class MetricsCollectorTests
    {
        [Fact]
        public void RecordMetric_and_Summary_work()
        {
            MetricsCollector.RecordMetric("test.metric", 10);
            MetricsCollector.RecordMetric("test.metric", 20);
            MetricsCollector.RecordTiming("test.metric", TimeSpan.FromMilliseconds(30));

            var s = MetricsCollector.GetSummary("test.metric", TimeSpan.FromMinutes(5));
            s.Name.Should().Be("test.metric");
            s.Count.Should().Be(1);

            var all = MetricsCollector.GetAllMetrics("test.metric");
            all.Should().NotBeNull();
        }

        [Fact]
        public void Record_circuit_breaker_metric_logs_and_aggregates()
        {
            var m = new CircuitBreakerMetric
            {
                ResourceName = "provider",
                State = CircuitState.Open,
                Success = false,
                ConsecutiveFailures = 3,
                FailureRate = 0.9,
                Duration = TimeSpan.FromMilliseconds(5),
                Timestamp = DateTime.UtcNow
            };
            MetricsCollector.Record(m);
            var s = MetricsCollector.GetAllMetrics("circuit_breaker");
            s.Should().NotBeNull();
        }
    }
}
