using Xunit;

namespace Brainarr.Tests.Services.Resilience
{
    /// <summary>
    /// Collection that disables parallel execution for resilience tests.
    ///
    /// ## WHY THIS EXISTS
    ///
    /// Tests in this collection verify concurrent behavior (e.g., semaphore-based
    /// concurrency caps, rate limiter interactions). When xUnit runs these tests
    /// in parallel with OTHER test classes, the background thread pool pressure
    /// causes non-deterministic timing failures:
    /// - "max concurrent sends was 3, expected <= 2" (thread scheduling variance)
    /// - Rate limiter window boundary races
    ///
    /// By grouping these tests in a single collection with DisableParallelization,
    /// they run sequentially relative to each other AND without interference from
    /// unrelated tests running on other threads.
    ///
    /// ## POLICY FOR ADDING TESTS
    ///
    /// Only add tests to this collection if they:
    /// 1. Verify concurrent behavior (parallelism caps, race conditions)
    /// 2. Use shared static state that can't be isolated per-test
    /// 3. Have demonstrated flakiness due to parallel execution
    ///
    /// Do NOT add tests here just because they use async/await or have timing.
    /// Most timing-sensitive tests should use deterministic controls (FakeTimeProvider,
    /// TaskCompletionSource gates) rather than collection isolation.
    ///
    /// ## TECH DEBT NOTE
    ///
    /// Long-term, consider replacing this isolation with:
    /// - FakeTimeProvider for time-sensitive tests (Common's testkit has one)
    /// - Semaphore gates for coordination instead of Task.Delay
    /// - Per-test SemaphoreSlim instances instead of static pools
    ///
    /// See: docs/decisions/ADR-xxx-deterministic-testing.md (future)
    /// </summary>
    [CollectionDefinition("ResilienceTests", DisableParallelization = true)]
    public sealed class ResilienceTestCollection
    {
    }
}
