using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.TestKit.Testing;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;
using CommonOptions = Lidarr.Plugin.Common.Services.Resilience.AdvancedCircuitBreakerOptions;

namespace Brainarr.Tests.Services.Resilience
{
    /// <summary>
    /// Characterization tests that lock down the circuit breaker behavior.
    /// These tests document existing semantics and serve as a gate for the WS4.2 migration.
    /// Tests verify the BrainarrCircuitBreakerAdapter preserves the original behavior.
    /// All timing-sensitive tests use FakeTimeProvider for deterministic execution.
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class CircuitBreakerCharacterizationTests
    {
        private static Logger L => LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Creates a BrainarrCircuitBreakerAdapter with options mapped from CircuitBreakerOptions.
        /// This mirrors how CommonBreakerRegistry creates adapters in production.
        /// </summary>
        private static ICircuitBreaker CreateBreaker(
            string resourceName,
            CircuitBreakerOptions options,
            TimeProvider? timeProvider = null)
        {
            var commonOptions = new CommonOptions
            {
                ConsecutiveFailureThreshold = options?.FailureThreshold ?? 5,
                FailureRateThreshold = options?.FailureRateThreshold ?? BrainarrConstants.CircuitBreakerFailureThreshold,
                MinimumThroughput = options?.MinimumThroughput ?? BrainarrConstants.CircuitBreakerMinimumThroughput,
                SamplingWindowSize = options?.SamplingWindowSize ?? BrainarrConstants.CircuitBreakerSamplingWindow,
                BreakDuration = options?.BreakDuration ?? TimeSpan.FromSeconds(BrainarrConstants.CircuitBreakerDurationSeconds),
                HalfOpenSuccessThreshold = options?.HalfOpenSuccessThreshold ?? 3
            };
            return new BrainarrCircuitBreakerAdapter(resourceName, commonOptions, L, timeProvider);
        }

        [Fact]
        public void Starts_Closed()
        {
            var cb = CreateBreaker("ai:test:model", CircuitBreakerOptions.Default);

            cb.State.Should().Be(CircuitState.Closed);
            cb.ConsecutiveFailures.Should().Be(0);
            cb.FailureRate.Should().Be(0);
        }

        [Fact]
        public async Task Opens_After_Handled_Exception_And_Blocks_While_Open()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromMinutes(10),
                HalfOpenSuccessThreshold = 1,
                SamplingWindowSize = 10,
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options);

            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException("timeout"))));

            cb.State.Should().Be(CircuitState.Open);

            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
                await cb.ExecuteAsync(() => Task.FromResult(42)));
        }

        [Fact]
        public async Task HalfOpen_Success_Closes_When_BreakDuration_Elapsed()
        {
            var fakeTime = new FakeTimeProvider();
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromSeconds(30),
                HalfOpenSuccessThreshold = 1,
                SamplingWindowSize = 10,
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options, fakeTime);

            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException("timeout"))));
            cb.State.Should().Be(CircuitState.Open);

            // Advance time past break duration - circuit transitions to half-open
            fakeTime.Advance(TimeSpan.FromSeconds(31));

            // Next successful call closes the circuit
            var result = await cb.ExecuteAsync(() => Task.FromResult(42));
            result.Should().Be(42);
            cb.State.Should().Be(CircuitState.Closed);
        }

        [Fact]
        public async Task ExecuteWithFallback_Returns_Fallback_When_Open_And_Does_Not_Invoke_Operation()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromMinutes(10),
                HalfOpenSuccessThreshold = 1,
                SamplingWindowSize = 10,
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options);

            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException("timeout"))));

            var invoked = false;
            var fallback = await cb.ExecuteWithFallbackAsync(
                () =>
                {
                    invoked = true;
                    return Task.FromResult(123);
                },
                fallbackValue: 7);

            fallback.Should().Be(7);
            invoked.Should().BeFalse();
        }

        [Fact]
        public async Task CircuitOpened_And_Closed_Events_Fire()
        {
            var fakeTime = new FakeTimeProvider();
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromSeconds(30),
                HalfOpenSuccessThreshold = 1,
                SamplingWindowSize = 10,
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options, fakeTime);

            CircuitBreakerEventArgs? opened = null;
            CircuitBreakerEventArgs? closed = null;

            cb.CircuitOpened += (_, args) => opened = args;
            cb.CircuitClosed += (_, args) => closed = args;

            await Assert.ThrowsAsync<HttpRequestException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new HttpRequestException("network"))));

            opened.Should().NotBeNull();
            opened!.ResourceName.Should().Be("ai:test:model");
            opened.State.Should().Be(CircuitState.Open);

            // Advance time past break duration
            fakeTime.Advance(TimeSpan.FromSeconds(31));

            await cb.ExecuteAsync(() => Task.FromResult(1));

            closed.Should().NotBeNull();
            closed!.ResourceName.Should().Be("ai:test:model");
            closed.State.Should().Be(CircuitState.Closed);
        }

        [Fact]
        public async Task Reset_Closes_And_Clears_Statistics()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromMinutes(10),
                HalfOpenSuccessThreshold = 1,
                SamplingWindowSize = 10,
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options);

            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException("timeout"))));

            cb.State.Should().Be(CircuitState.Open);
            cb.Reset();

            cb.State.Should().Be(CircuitState.Closed);
            cb.ConsecutiveFailures.Should().Be(0);
            cb.GetStatistics().TotalOperations.Should().Be(0);
        }

        #region Keying Scheme Tests

        [Fact]
        public void ResourceName_Uses_Keying_Format()
        {
            // The keying format is "ai:{provider}:{modelId}" as established in BreakerRegistry
            var cb = CreateBreaker("ai:openai:gpt-4", CircuitBreakerOptions.Default);
            cb.ResourceName.Should().Be("ai:openai:gpt-4");
        }

        [Theory]
        [InlineData("ai:anthropic:claude-3-opus")]
        [InlineData("ai:ollama:llama2")]
        [InlineData("ai:deepseek:deepseek-chat")]
        public void ResourceName_Preserved_For_Any_Provider_Model_Combination(string resourceName)
        {
            var cb = CreateBreaker(resourceName, CircuitBreakerOptions.Default);
            cb.ResourceName.Should().Be(resourceName);
        }

        #endregion

        #region Failure Classification Tests

        [Fact]
        public async Task TaskCanceledException_Is_Treated_As_Failure()
        {
            // CURRENT BEHAVIOR: TaskCanceledException trips the breaker
            // This may be surprising - cancellation is treated same as failure
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromMinutes(10),
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options);

            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TaskCanceledException("cancelled"))));

            cb.State.Should().Be(CircuitState.Open, "TaskCanceledException is treated as a failure");
            cb.ConsecutiveFailures.Should().Be(1);
        }

        [Fact]
        public async Task HttpRequestException_Is_Treated_As_Failure()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromMinutes(10),
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options);

            await Assert.ThrowsAsync<HttpRequestException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new HttpRequestException("network error"))));

            cb.State.Should().Be(CircuitState.Open);
            cb.ConsecutiveFailures.Should().Be(1);
        }

        [Fact]
        public async Task Client_Error_With_BadRequest_Does_Not_Trip_Breaker()
        {
            // CURRENT BEHAVIOR: HttpRequestException with "4" AND "Bad Request" in message is excluded
            // This is brittle string-based client error detection (should use status codes in future)
            //
            // NOTE: This test is intentionally message-coupled because production uses message-based
            // detection (ex.Message.Contains("4") && ex.Message.Contains("Bad Request")). Do not
            // "simplify" to status-code checks unless production is also refactored.
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromMinutes(10),
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options);

            // HttpRequestException (normally a handled type) with "4" + "Bad Request" - excluded by string match
            var clientError = new HttpRequestException("400 Bad Request");
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(clientError)));

            cb.State.Should().Be(CircuitState.Closed, "Client errors excluded by string matching");
            cb.ConsecutiveFailures.Should().Be(0);
        }

        [Fact]
        public async Task Generic_Exception_Without_BadRequest_Does_Not_Trip_Breaker()
        {
            // Non-handled exceptions pass through without recording failure
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromMinutes(10),
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options);

            var genericError = new InvalidOperationException("some logic error");
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(genericError)));

            cb.State.Should().Be(CircuitState.Closed, "Non-handled exceptions don't trip breaker");
            cb.ConsecutiveFailures.Should().Be(0, "Non-handled exceptions don't record as failures");
        }

        #endregion

        #region Consecutive Failures Threshold Tests

        [Fact]
        public async Task Opens_After_Consecutive_Failures_Threshold()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 5, // Default
                FailureRateThreshold = 1.0, // Disable rate-based opening
                BreakDuration = TimeSpan.FromMinutes(10),
                SamplingWindowSize = 100, // Match MinimumThroughput for validation
                MinimumThroughput = 100 // High minimum to prevent rate-based opening
            };
            var cb = CreateBreaker("ai:test:model", options);

            // 4 failures - should remain closed
            for (int i = 0; i < 4; i++)
            {
                await Assert.ThrowsAsync<TimeoutException>(async () =>
                    await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));
            }
            cb.State.Should().Be(CircuitState.Closed);
            cb.ConsecutiveFailures.Should().Be(4);

            // 5th failure - opens the circuit
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));

            cb.State.Should().Be(CircuitState.Open);
            cb.ConsecutiveFailures.Should().Be(5);
        }

        [Fact]
        public async Task Success_Resets_Consecutive_Failure_Counter()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 5,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromMinutes(10),
                SamplingWindowSize = 100, // Match MinimumThroughput for validation
                MinimumThroughput = 100
            };
            var cb = CreateBreaker("ai:test:model", options);

            // 3 failures
            for (int i = 0; i < 3; i++)
            {
                await Assert.ThrowsAsync<TimeoutException>(async () =>
                    await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));
            }
            cb.ConsecutiveFailures.Should().Be(3);

            // 1 success resets counter
            await cb.ExecuteAsync(() => Task.FromResult(42));
            cb.ConsecutiveFailures.Should().Be(0);

            // Circuit should still be closed
            cb.State.Should().Be(CircuitState.Closed);
        }

        #endregion

        #region Failure Rate Threshold Tests

        [Fact]
        public async Task Opens_When_Failure_Rate_Exceeds_Threshold_And_Minimum_Throughput_Met()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 100, // High to prevent consecutive-based opening
                FailureRateThreshold = 0.5, // 50%
                BreakDuration = TimeSpan.FromMinutes(10),
                SamplingWindowSize = 20,
                MinimumThroughput = 10
            };
            var cb = CreateBreaker("ai:test:model", options);

            // 5 successes
            for (int i = 0; i < 5; i++)
            {
                await cb.ExecuteAsync(() => Task.FromResult(i));
            }
            cb.State.Should().Be(CircuitState.Closed);

            // 4 failures (4/9 = 44% < 50%, circuit stays closed)
            for (int i = 0; i < 4; i++)
            {
                await Assert.ThrowsAsync<TimeoutException>(async () =>
                    await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));
            }
            cb.State.Should().Be(CircuitState.Closed, "9 ops at 44% failure rate - below threshold");

            // 1 more failure (5/10 = 50%, meets threshold and minimum throughput)
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));

            cb.State.Should().Be(CircuitState.Open, "10 ops at 50% failure rate - meets threshold");
            cb.FailureRate.Should().BeApproximately(0.5, 0.01);
        }

        [Fact]
        public async Task Does_Not_Open_On_High_Failure_Rate_Below_Minimum_Throughput()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 100, // High to prevent consecutive-based opening
                FailureRateThreshold = 0.5, // 50%
                BreakDuration = TimeSpan.FromMinutes(10),
                SamplingWindowSize = 20,
                MinimumThroughput = 10
            };
            var cb = CreateBreaker("ai:test:model", options);

            // 1 success, 3 failures (75% failure rate but only 4 ops < 10 minimum)
            await cb.ExecuteAsync(() => Task.FromResult(1));
            for (int i = 0; i < 3; i++)
            {
                await Assert.ThrowsAsync<TimeoutException>(async () =>
                    await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));
            }

            cb.FailureRate.Should().BeApproximately(0.75, 0.01);
            cb.State.Should().Be(CircuitState.Closed, "Below minimum throughput - rate-based opening disabled");
        }

        #endregion

        #region Half-Open State Transition Tests

        [Fact]
        public async Task HalfOpen_Closes_After_Configured_Successes()
        {
            var fakeTime = new FakeTimeProvider();
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromSeconds(30),
                HalfOpenSuccessThreshold = 3,
                SamplingWindowSize = 10,
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options, fakeTime);

            // Open the circuit
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));
            cb.State.Should().Be(CircuitState.Open);

            // Advance time past break duration
            fakeTime.Advance(TimeSpan.FromSeconds(31));

            // First success - transitions to half-open, stays half-open
            await cb.ExecuteAsync(() => Task.FromResult(1));
            cb.State.Should().Be(CircuitState.HalfOpen, "1 success in half-open, need 3 to close");

            // Second success
            await cb.ExecuteAsync(() => Task.FromResult(2));
            cb.State.Should().Be(CircuitState.HalfOpen, "2 successes in half-open, need 3 to close");

            // Third success - closes circuit
            await cb.ExecuteAsync(() => Task.FromResult(3));
            cb.State.Should().Be(CircuitState.Closed, "3 successes closes the circuit");
        }

        [Fact]
        public async Task HalfOpen_Failure_Immediately_Reopens()
        {
            var fakeTime = new FakeTimeProvider();
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                FailureRateThreshold = 1.0,
                BreakDuration = TimeSpan.FromSeconds(30),
                HalfOpenSuccessThreshold = 3,
                SamplingWindowSize = 10,
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options, fakeTime);

            // Open the circuit
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));
            cb.State.Should().Be(CircuitState.Open);

            // Advance time past break duration
            fakeTime.Advance(TimeSpan.FromSeconds(31));

            // 1 success to enter half-open
            await cb.ExecuteAsync(() => Task.FromResult(1));
            cb.State.Should().Be(CircuitState.HalfOpen);

            // Failure in half-open immediately reopens
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));

            cb.State.Should().Be(CircuitState.Open, "Any failure in half-open reopens the circuit");
        }

        #endregion

        #region Windowing / CircularBuffer Tests

        [Fact]
        public async Task CircularBuffer_Wraps_And_Maintains_Accurate_FailureRate()
        {
            // This test verifies that failure rate is calculated over the sliding window,
            // and that old operations get pushed out as new ones arrive.
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 100, // High to prevent consecutive-based opening
                FailureRateThreshold = 0.7, // 70% - higher threshold to observe window behavior
                BreakDuration = TimeSpan.FromMinutes(10),
                SamplingWindowSize = 5, // Small window for testing
                MinimumThroughput = 3
            };
            var cb = CreateBreaker("ai:test:model", options);

            // Phase 1: Fill buffer with 3 successes, 2 failures = 40% failure rate
            // Window: [S, S, S, F, F]
            for (int i = 0; i < 3; i++) await cb.ExecuteAsync(() => Task.FromResult(i));
            for (int i = 0; i < 2; i++)
            {
                await Assert.ThrowsAsync<TimeoutException>(async () =>
                    await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));
            }

            cb.FailureRate.Should().BeApproximately(0.4, 0.01);
            cb.State.Should().Be(CircuitState.Closed);

            // Phase 2: Add 1 more failure - pushes out oldest success
            // Window: [S, S, F, F, F] = 60% failure rate (still below 70%)
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));

            cb.FailureRate.Should().BeApproximately(0.6, 0.01);
            cb.State.Should().Be(CircuitState.Closed, "60% is below 70% threshold");

            // Phase 3: Add 1 more failure - pushes out another success
            // Window: [S, F, F, F, F] = 80% failure rate (exceeds 70%)
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));

            cb.FailureRate.Should().BeGreaterThanOrEqualTo(0.7);
            cb.State.Should().Be(CircuitState.Open, "Window wrapped, failure rate now exceeds threshold");
        }

        [Fact]
        public void GetStatistics_Returns_Correct_Initial_State()
        {
            var options = new CircuitBreakerOptions { SamplingWindowSize = 10 };
            var cb = CreateBreaker("ai:test:model", options);

            var stats = cb.GetStatistics();
            stats.ResourceName.Should().Be("ai:test:model");
            stats.State.Should().Be(CircuitState.Closed);
            stats.TotalOperations.Should().Be(0);
            stats.ConsecutiveFailures.Should().Be(0);
            stats.FailureRate.Should().Be(0);
            stats.NextHalfOpenAttempt.Should().BeNull();
        }

        /// <summary>
        /// Verifies the statistics contract isn't silently degraded after migration.
        /// RecentOperations may be null (not exposed by Common), but core fields must be present.
        /// </summary>
        [Fact]
        public async Task GetStatistics_Returns_NonNull_Core_Fields_After_Operations()
        {
            var options = new CircuitBreakerOptions
            {
                FailureThreshold = 5,
                SamplingWindowSize = 10,
                MinimumThroughput = 1
            };
            var cb = CreateBreaker("ai:test:model", options);

            // Execute some operations
            await cb.ExecuteAsync(() => Task.FromResult(1));
            await cb.ExecuteAsync(() => Task.FromResult(2));
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await cb.ExecuteAsync<int>(() => Task.FromException<int>(new TimeoutException())));

            var stats = cb.GetStatistics();

            // Core fields must be non-null and sensible
            stats.ResourceName.Should().NotBeNullOrEmpty();
            stats.State.Should().Be(CircuitState.Closed);
            stats.TotalOperations.Should().Be(3);
            stats.ConsecutiveFailures.Should().Be(1);
            stats.FailureRate.Should().BeApproximately(1.0 / 3.0, 0.01);
            stats.LastStateChange.Should().BeAfter(DateTime.MinValue);
            // RecentOperations may be null (documented behavior after migration)
        }

        #endregion

        #region Configuration Constants Tests

        [Fact]
        public void Default_Options_Use_Brainarr_Constants()
        {
            // Document the default configuration values from BrainarrConstants
            var defaults = CircuitBreakerOptions.Default;

            // These values come from BrainarrConstants
            defaults.FailureThreshold.Should().Be(5, "consecutive failures to open");
            defaults.FailureRateThreshold.Should().Be(0.5, "50% failure rate threshold");
            defaults.BreakDuration.Should().Be(TimeSpan.FromSeconds(30), "30 second open duration");
            defaults.HalfOpenSuccessThreshold.Should().Be(3, "3 successes to close from half-open");
            defaults.SamplingWindowSize.Should().Be(20, "20 operation sampling window");
            defaults.MinimumThroughput.Should().Be(10, "10 minimum operations for rate-based opening");
        }

        [Fact]
        public void Aggressive_Options_Presets()
        {
            var aggressive = CircuitBreakerOptions.Aggressive;

            aggressive.FailureThreshold.Should().Be(3);
            aggressive.FailureRateThreshold.Should().Be(0.3);
            aggressive.BreakDuration.Should().Be(TimeSpan.FromMinutes(5));
        }

        [Fact]
        public void Lenient_Options_Presets()
        {
            var lenient = CircuitBreakerOptions.Lenient;

            lenient.FailureThreshold.Should().Be(10);
            lenient.FailureRateThreshold.Should().Be(0.75);
            lenient.BreakDuration.Should().Be(TimeSpan.FromSeconds(30));
        }

        #endregion
    }
}
