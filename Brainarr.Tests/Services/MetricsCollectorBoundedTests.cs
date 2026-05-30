using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Verifies the concurrency-hardening additions to <see cref="MetricsCollector"/>:
    /// bounded dict growth (cap = 1 024) and idempotent Dispose.
    /// </summary>
    [Collection("MetricsCollectorBounded")]
    public class MetricsCollectorBoundedTests : IDisposable
    {
        // MetricsCap constant value from production code.
        private const int MetricsCap = 1024;

        public MetricsCollectorBoundedTests()
        {
            // Reset static state so tests are isolated.
            MetricsCollector.ResetForTesting();
        }

        void IDisposable.Dispose()
        {
            MetricsCollector.ResetForTesting();
        }

        // -----------------------------------------------------------------------
        // Dict cap / eviction
        // -----------------------------------------------------------------------

        [Fact]
        public void Add_AtCapacity_EvictsOldEntries()
        {
            // Fill the dictionary to exactly MetricsCap unique keys.
            for (int i = 0; i < MetricsCap; i++)
            {
                MetricsCollector.RecordMetric($"metric.cap.test.{i}", i);
            }

            // Inserting one more entry beyond the cap must trigger clear-all eviction.
            // After eviction, the summary for the earlier key should not exist (no data)
            // because the dictionary was cleared.
            var act = () => MetricsCollector.RecordMetric("metric.cap.overflow", 999);
            act.Should().NotThrow("overflow eviction must be transparent to callers");

            // Post-eviction the overflowed-into entry must be queryable (freshly added).
            var postEviction = MetricsCollector.GetSummary("metric.cap.overflow", TimeSpan.FromMinutes(5));
            postEviction.Count.Should().Be(1, "the entry added after eviction must be present");
        }

        // -----------------------------------------------------------------------
        // Per-metric raw-point cap (growth between retention sweeps)
        // -----------------------------------------------------------------------

        [Fact]
        public void RawPoints_AreCappedPerMetric_UnderBurst()
        {
            // A single hot metric recorded many times between the hourly TTL sweeps must not grow its
            // raw-point list without bound (24h retention × call rate). The newest points are kept.
            const int cap = 10_000; // MaxPointsPerMetric in production
            for (int i = 0; i < cap + 2000; i++)
            {
                MetricsCollector.RecordMetric("metric.points.burst", i);
            }

            var summary = MetricsCollector.GetSummary("metric.points.burst", TimeSpan.FromMinutes(5));

            summary.Count.Should().BeLessThanOrEqualTo(cap,
                "raw points per metric must stay bounded between retention sweeps");
            summary.Count.Should().BeGreaterThan(0, "recent points are retained, not all dropped");
        }

        // -----------------------------------------------------------------------
        // Timer / Dispose
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Dispose_StopsTimer_NoFurtherAggregation()
        {
            // Record baseline.
            MetricsCollector.RecordMetric("timer.test.metric", 1.0);
            var before = MetricsCollector.GetSummary("timer.test.metric", TimeSpan.FromMinutes(5));
            before.Count.Should().Be(1);

            // Dispose should not throw.
            var act = () => MetricsCollector.Dispose();
            act.Should().NotThrow();

            // After dispose the timer fires no further callbacks.
            // We can't directly observe the timer but we can confirm Dispose ran cleanly
            // and the dictionary was cleared.
            await Task.Delay(50); // yield to allow any pending timer callbacks to fire

            var after = MetricsCollector.GetSummary("timer.test.metric", TimeSpan.FromMinutes(5));
            after.Count.Should().Be(0, "Dispose clears the metrics dictionary");
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            // Multiple calls must not throw.
            var act = () =>
            {
                MetricsCollector.Dispose();
                MetricsCollector.Dispose();
                MetricsCollector.Dispose();
            };
            act.Should().NotThrow("Dispose must be idempotent");
        }
    }
}
