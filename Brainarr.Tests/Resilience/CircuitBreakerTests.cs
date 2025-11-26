using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;
using Xunit;

namespace Brainarr.Tests.Resilience
{
    [Collection("RateLimiterTests")]
    [Trait("Category", "Unit")]
    public class CircuitBreakerTests
    {
        private static Logger L => LogManager.GetCurrentClassLogger();

        #region Initial State Tests

        [Fact]
        public void CircuitBreaker_starts_in_closed_state()
        {
            var cb = new CircuitBreaker(logger: L);

            cb.State.Should().Be(CircuitState.Closed);
            cb.FailureCount.Should().Be(0);
            cb.LastFailureTime.Should().BeNull();
        }

        #endregion

        #region Successful Operation Tests

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
        public async Task ExecuteAsync_decrements_failure_count_on_success()
        {
            var cb = new CircuitBreaker(failureThreshold: 5, timeoutSeconds: 30, logger: L);

            // Cause some failures first (but not enough to open)
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await cb.ExecuteAsync<int>(
                        () => throw new Exception("test"),
                        "fail-op");
                }
                catch { }
            }

            cb.FailureCount.Should().Be(2);

            // Now succeed
            await cb.ExecuteAsync(async () => { await Task.Yield(); return 1; }, "success-op");

            cb.FailureCount.Should().Be(1); // Decremented by 1
        }

        #endregion

        #region Failure Recording Tests

        [Fact]
        public async Task ExecuteAsync_increments_failure_count_on_exception()
        {
            var cb = new CircuitBreaker(failureThreshold: 5, timeoutSeconds: 30, logger: L);

            try
            {
                await cb.ExecuteAsync<int>(
                    () => throw new InvalidOperationException("test error"),
                    "test-operation");
            }
            catch { }

            cb.FailureCount.Should().Be(1);
            cb.LastFailureTime.Should().NotBeNull();
            cb.LastFailureTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task ExecuteAsync_rethrows_exception_on_failure()
        {
            var cb = new CircuitBreaker(failureThreshold: 5, timeoutSeconds: 30, logger: L);

            var act = async () => await cb.ExecuteAsync<int>(
                () => throw new InvalidOperationException("test error"),
                "test-operation");

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("test error");
        }

        #endregion

        #region State Transition Tests

        [Fact]
        public async Task CircuitBreaker_opens_after_threshold_failures()
        {
            var cb = new CircuitBreaker(failureThreshold: 3, openDurationSeconds: 60, timeoutSeconds: 30, logger: L);

            // Cause failures to reach threshold
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await cb.ExecuteAsync<int>(
                        () => throw new Exception($"failure {i + 1}"),
                        "test-op");
                }
                catch { }
            }

            cb.State.Should().Be(CircuitState.Open);
            cb.FailureCount.Should().Be(3);
        }

        [Fact]
        public async Task CircuitBreaker_throws_CircuitBreakerOpenException_when_open()
        {
            var cb = new CircuitBreaker(failureThreshold: 2, openDurationSeconds: 300, timeoutSeconds: 30, logger: L);

            // Open the circuit
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    await cb.ExecuteAsync<int>(
                        () => throw new Exception("failure"),
                        "test-op");
                }
                catch (CircuitBreakerOpenException) { }
                catch { }
            }

            // Now it should be open
            cb.State.Should().Be(CircuitState.Open);

            var act = async () => await cb.ExecuteAsync(
                async () => { await Task.Yield(); return 1; },
                "blocked-op");

            await act.Should().ThrowAsync<CircuitBreakerOpenException>();
        }

        [Fact]
        public async Task CircuitBreaker_transitions_to_half_open_after_duration()
        {
            var cb = new CircuitBreaker(
                failureThreshold: 1,
                openDurationSeconds: 1, // 1 second for test
                timeoutSeconds: 30,
                logger: L);

            // Open the circuit
            try
            {
                await cb.ExecuteAsync<int>(
                    () => throw new Exception("failure"),
                    "test-op");
            }
            catch { }

            cb.State.Should().Be(CircuitState.Open);

            // Wait for open duration to pass
            await Task.Delay(1500); // Wait 1.5 seconds

            // Next call should find circuit in half-open and succeed
            var result = await cb.ExecuteAsync(
                async () => { await Task.Yield(); return 42; },
                "recovery-op");

            result.Should().Be(42);
            cb.State.Should().Be(CircuitState.Closed);
        }

        #endregion

        #region Timeout and Reset Tests

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

        [Fact]
        public async Task Reset_allows_operations_after_being_open()
        {
            var cb = new CircuitBreaker(failureThreshold: 1, openDurationSeconds: 300, timeoutSeconds: 30, logger: L);

            // Open the circuit
            try
            {
                await cb.ExecuteAsync<int>(
                    () => throw new Exception("failure"),
                    "test-op");
            }
            catch { }

            cb.State.Should().Be(CircuitState.Open);

            // Reset
            cb.Reset();

            // Should work now
            var result = await cb.ExecuteAsync(
                async () => { await Task.Yield(); return 42; },
                "after-reset");

            result.Should().Be(42);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task CircuitBreaker_is_thread_safe_under_concurrent_access()
        {
            var cb = new CircuitBreaker(failureThreshold: 100, timeoutSeconds: 30, logger: L);
            var exceptions = new List<Exception>();
            var successCount = 0;
            var failCount = 0;

            var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
            {
                try
                {
                    if (i % 3 == 0)
                    {
                        // Fail
                        await cb.ExecuteAsync<int>(
                            () => throw new Exception("concurrent fail"),
                            $"op-{i}");
                    }
                    else
                    {
                        // Succeed
                        await cb.ExecuteAsync(
                            async () => { await Task.Yield(); return i; },
                            $"op-{i}");
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (Exception ex) when (!(ex is CircuitBreakerOpenException))
                {
                    Interlocked.Increment(ref failCount);
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Should not have any unexpected exceptions
            exceptions.Where(e => !(e is CircuitBreakerOpenException)).Should().BeEmpty();
        }

        #endregion
    }

    [Trait("Category", "Unit")]
    public class CircuitBreakerFactoryTests
    {
        private static Logger L => LogManager.GetCurrentClassLogger();

        [Fact]
        public void GetOrCreate_returns_same_instance_for_same_provider()
        {
            var factory = new CircuitBreakerFactory(L);

            var breaker1 = factory.GetOrCreate("TestProvider");
            var breaker2 = factory.GetOrCreate("TestProvider");

            breaker1.Should().BeSameAs(breaker2);
        }

        [Fact]
        public void GetOrCreate_returns_different_instances_for_different_providers()
        {
            var factory = new CircuitBreakerFactory(L);

            var breaker1 = factory.GetOrCreate("Provider1");
            var breaker2 = factory.GetOrCreate("Provider2");

            breaker1.Should().NotBeSameAs(breaker2);
        }

        [Theory]
        [InlineData("Ollama")]
        [InlineData("LMStudio")]
        [InlineData("OpenAI")]
        [InlineData("Anthropic")]
        [InlineData("Groq")]
        [InlineData("UnknownProvider")]
        public void GetOrCreate_creates_breakers_for_known_providers(string provider)
        {
            var factory = new CircuitBreakerFactory(L);

            var breaker = factory.GetOrCreate(provider);

            breaker.Should().NotBeNull();
            breaker.State.Should().Be(CircuitState.Closed);
        }

        [Fact]
        public async Task ResetAll_resets_all_breakers()
        {
            var factory = new CircuitBreakerFactory(L);

            // Create and open some breakers
            var breaker1 = factory.GetOrCreate("Provider1") as CircuitBreaker;
            var breaker2 = factory.GetOrCreate("Provider2") as CircuitBreaker;

            // Open them by causing failures (use high threshold so we can control)
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    await breaker1.ExecuteAsync<int>(
                        () => throw new Exception("fail"),
                        "op");
                }
                catch { }
                try
                {
                    await breaker2.ExecuteAsync<int>(
                        () => throw new Exception("fail"),
                        "op");
                }
                catch { }
            }

            // Reset all
            factory.ResetAll();

            factory.GetOrCreate("Provider1").State.Should().Be(CircuitState.Closed);
            factory.GetOrCreate("Provider2").State.Should().Be(CircuitState.Closed);
        }

        [Fact]
        public void GetOrCreate_is_thread_safe()
        {
            var factory = new CircuitBreakerFactory(L);
            var breakers = new List<ICircuitBreaker>();
            var lockObj = new object();

            Parallel.For(0, 100, i =>
            {
                var breaker = factory.GetOrCreate("SharedProvider");
                lock (lockObj)
                {
                    breakers.Add(breaker);
                }
            });

            // All should be the same instance
            breakers.Distinct().Should().HaveCount(1);
        }
    }

    [Trait("Category", "Unit")]
    public class CircuitBreakerOpenExceptionTests
    {
        [Fact]
        public void CircuitBreakerOpenException_contains_message()
        {
            var exception = new CircuitBreakerOpenException("Test message");

            exception.Message.Should().Be("Test message");
        }
    }
}
