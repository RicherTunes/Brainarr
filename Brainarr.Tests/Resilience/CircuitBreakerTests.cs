using System;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;
using Xunit;

namespace Brainarr.Tests.Resilience
{
    [Collection("RateLimiterTests")]
    public class CircuitBreakerTests
    {
        private static Logger L => LogManager.GetCurrentClassLogger();

        [Fact]
        public async Task Closed_state_success_returns_value()
        {
            var cb = new CircuitBreaker(failureThreshold: 3, openDurationSeconds: 1, timeoutSeconds: 5, logger: L);
            var result = await cb.ExecuteAsync(async () => { await Task.Yield(); return 42; }, "op");
            result.Should().Be(42);
            cb.State.Should().Be(CircuitState.Closed);
            cb.FailureCount.Should().Be(0);
        }

        [Fact]
        public async Task Timeout_opens_circuit_and_reset_allows_success()
        {
            // Immediate timeout and open
            var cb = new CircuitBreaker(failureThreshold: 1, openDurationSeconds: 0, timeoutSeconds: 3, logger: L);

            // First call times out -> opens
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync(async () => { await Task.Delay(4000); return 1; }, "slow")
            );
            cb.State.Should().Be(CircuitState.Open);
            cb.FailureCount.Should().BeGreaterThan(0);

            // Manual reset allows success deterministically
            cb.Reset();
            cb.State.Should().Be(CircuitState.Closed);
            var ok = await cb.ExecuteAsync(async () => { await Task.Delay(100); return 7; }, "recover");
            ok.Should().Be(7);
        }

        [Fact]
        public async Task Half_open_failure_reopens_circuit()
        {
            var cb = new CircuitBreaker(failureThreshold: 1, openDurationSeconds: 0, timeoutSeconds: 0, logger: L);

            // Open
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync(async () => { await Task.Delay(2000); return 1; }, "slow")
            );
            cb.State.Should().Be(CircuitState.Open);

            // Half-open attempt fails -> re-open
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync(async () => { await Task.Delay(2000); return 1; }, "still-slow")
            );
            cb.State.Should().Be(CircuitState.Open);
        }

        [Fact]
        public void Reset_clears_state()
        {
            var cb = new CircuitBreaker(failureThreshold: 1, openDurationSeconds: 10, timeoutSeconds: 0, logger: L);
            cb.Reset();
            cb.State.Should().Be(CircuitState.Closed);
            cb.FailureCount.Should().Be(0);
            cb.LastFailureTime.Should().BeNull();
        }
    }
}
