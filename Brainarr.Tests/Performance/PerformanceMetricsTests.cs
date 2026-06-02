using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Performance;
using Xunit;

namespace Brainarr.Tests.Performance
{
    /// <summary>
    /// Pure-function tests for <see cref="PerformanceMetrics"/> slow-response detection.
    /// Uses the deterministic <see cref="PerformanceMetrics.IsSlowResponse"/> predicate rather than
    /// an NLog-capture test (the repo has known NLogTestLogger flakiness).
    /// </summary>
    public class PerformanceMetricsTests
    {
        [Fact]
        public void IsSlowResponse_ThresholdBoundary()
        {
            // Normal reasoning-model latency (GLM/o1-style) routinely lands at ~16-70s.
            // The live logs proved a 70s response fired the WARN on ~100% of calls under the
            // old 10s threshold; with the recalibrated threshold this MUST NOT warn.
            PerformanceMetrics.IsSlowResponse(TimeSpan.FromSeconds(70)).Should().BeFalse(
                "normal reasoning-model latency (~70s) must not trip the slow-response WARN");

            // A genuinely near-timeout response (default provider timeout ~120s) DOES warn.
            PerformanceMetrics.IsSlowResponse(TimeSpan.FromSeconds(95)).Should().BeTrue(
                "a response approaching the ~120s provider timeout is genuinely slow and must warn");

            // Boundary check just below / at / just above the 90s threshold.
            PerformanceMetrics.IsSlowResponse(TimeSpan.FromSeconds(89.9)).Should().BeFalse(
                "just below the threshold must not warn");
            PerformanceMetrics.IsSlowResponse(TimeSpan.FromSeconds(90)).Should().BeFalse(
                "exactly at the threshold must not warn (strictly greater-than)");
            PerformanceMetrics.IsSlowResponse(TimeSpan.FromSeconds(90.1)).Should().BeTrue(
                "just above the threshold must warn");
        }
    }
}
