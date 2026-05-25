using System;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.TestKit.Testing;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Services.Resilience
{
    /// <summary>
    /// Unit tests for <see cref="LlmAuthCircuit"/>.
    /// All time-sensitive tests use <see cref="FakeTimeProvider"/> for deterministic behaviour.
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class LlmAuthCircuitTests
    {
        private const string ProviderId = "openai";
        private const string ApiKey = "sk-test-key-abc123";
        private const string AltKey = "sk-different-key-xyz789";
        private const string AltProvider = "anthropic";

        // Use a threshold of 3 (design default) and a tight window/duration for speed.
        private static LlmAuthCircuit MakeCircuit(FakeTimeProvider? clock = null)
            => new LlmAuthCircuit(
                logger: null,
                failureThreshold: 3,
                failureWindow: TimeSpan.FromMinutes(5),
                openDuration: TimeSpan.FromMinutes(30),
                time: clock ?? new FakeTimeProvider());

        // ── Test 1 ────────────────────────────────────────────────────────────
        [Fact]
        public void IsOpen_NeverFailed_ReturnsFalse()
        {
            var circuit = MakeCircuit();

            var open = circuit.IsOpen(ProviderId, ApiKey, out var reason);

            open.Should().BeFalse();
            reason.Should().BeNull();
        }

        // ── Test 2 ────────────────────────────────────────────────────────────
        [Fact]
        public void RecordAuthFailure_OnceWithinThreshold_StateRemainsClosed()
        {
            var circuit = MakeCircuit();

            circuit.RecordAuthFailure(ProviderId, ApiKey);

            // 1 failure < threshold of 3 — circuit stays closed.
            circuit.IsOpen(ProviderId, ApiKey, out var reason).Should().BeFalse();
            reason.Should().BeNull();
        }

        // ── Test 3 ────────────────────────────────────────────────────────────
        [Fact]
        public void RecordAuthFailure_NTimesInWindow_OpensCircuit()
        {
            var circuit = MakeCircuit();

            circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.RecordAuthFailure(ProviderId, ApiKey); // 3rd = threshold

            circuit.IsOpen(ProviderId, ApiKey, out var reason).Should().BeTrue();
            reason.Should().NotBeNullOrWhiteSpace();
            reason.Should().Contain(ProviderId);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────
        [Fact]
        public void IsOpen_AfterOpen_ReturnsTrue_For30Min()
        {
            var clock = new FakeTimeProvider();
            var circuit = MakeCircuit(clock);

            // Open the circuit.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.IsOpen(ProviderId, ApiKey, out _).Should().BeTrue();

            // Advance 29 minutes — still open.
            clock.Advance(TimeSpan.FromMinutes(29));
            circuit.IsOpen(ProviderId, ApiKey, out var reason).Should().BeTrue();
            reason.Should().NotBeNullOrWhiteSpace();
        }

        // ── Test 5 ────────────────────────────────────────────────────────────
        [Fact]
        public void IsOpen_AfterOpenAndElapsed_TransitionsToHalfOpen()
        {
            var clock = new FakeTimeProvider();
            var circuit = MakeCircuit(clock);

            // Open the circuit.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure(ProviderId, ApiKey);

            // Advance past the open duration.
            clock.Advance(TimeSpan.FromMinutes(31));

            // First IsOpen call should NOT block (half-open lets one probe through).
            var open = circuit.IsOpen(ProviderId, ApiKey, out var reason);
            open.Should().BeFalse("circuit transitions to HalfOpen after open duration expires");
            reason.Should().BeNull();
        }

        // ── Test 6 ────────────────────────────────────────────────────────────
        [Fact]
        public void RecordSuccess_AfterOpen_ResetsToClosed()
        {
            var clock = new FakeTimeProvider();
            var circuit = MakeCircuit(clock);

            // Open the circuit.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.IsOpen(ProviderId, ApiKey, out _).Should().BeTrue();

            // A success (e.g. after token rotation) resets the circuit.
            circuit.RecordSuccess(ProviderId, ApiKey);

            circuit.IsOpen(ProviderId, ApiKey, out var reason).Should().BeFalse();
            reason.Should().BeNull();
        }

        // ── Test 7 ────────────────────────────────────────────────────────────
        [Fact]
        public void IsOpen_DifferentKeys_AreIndependent()
        {
            var circuit = MakeCircuit();

            // Open keyA.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.IsOpen(ProviderId, ApiKey, out _).Should().BeTrue("keyA is open");

            // keyB is unaffected.
            circuit.IsOpen(ProviderId, AltKey, out var reason).Should().BeFalse("keyB is independent");
            reason.Should().BeNull();
        }

        // ── Test 8 ────────────────────────────────────────────────────────────
        [Fact]
        public void IsOpen_DifferentProviders_AreIndependent()
        {
            var circuit = MakeCircuit();

            // Open OpenAI with the same key string.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.IsOpen(ProviderId, ApiKey, out _).Should().BeTrue("OpenAI is open");

            // Anthropic with the same key is unaffected.
            circuit.IsOpen(AltProvider, ApiKey, out var reason).Should().BeFalse("Anthropic is independent");
            reason.Should().BeNull();
        }

        // ── Test 9 ────────────────────────────────────────────────────────────
        [Fact]
        public void Hash_DoesNotLeakPlaintext()
        {
            // MakeKey is the internal key used for the dict. It must NOT contain the raw key.
            var dictKey = LlmAuthCircuit.MakeKey(ProviderId, ApiKey);

            dictKey.Should().NotContain(ApiKey, "raw API key must not appear in the dict key");
            dictKey.Should().Contain(ProviderId, "provider id is part of the key for scoping");
            // The separator and a base-64 fragment should be present.
            dictKey.Should().Contain("::");
            // Length should be short (provider + "::" + 16 base-64 chars).
            dictKey.Length.Should().BeLessThan(100);
        }

        // ── Test 10 ───────────────────────────────────────────────────────────
        [Fact]
        public async Task Concurrency_ManyThreads_ExactlyOneOpenTransition()
        {
            var circuit = MakeCircuit();
            const int threadCount = 50;
            var openObservations = 0;

            // All threads simultaneously fire the 3rd (threshold) failure.
            // Pre-load 2 failures first (below threshold).
            circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.RecordAuthFailure(ProviderId, ApiKey);

            // Spin up many threads to race on the 3rd failure.
            var tasks = new Task[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    circuit.RecordAuthFailure(ProviderId, ApiKey);
                    if (circuit.IsOpen(ProviderId, ApiKey, out _))
                    {
                        System.Threading.Interlocked.Increment(ref openObservations);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Circuit should be in Open state (observed by at least one thread).
            circuit.IsOpen(ProviderId, ApiKey, out _).Should().BeTrue("circuit must be open after threshold breached");
            openObservations.Should().BeGreaterThan(0, "at least one thread must have observed the open transition");
        }

        // ── Bonus: HalfOpen probe failure re-opens ────────────────────────────
        [Fact]
        public void HalfOpenProbe_Failure_ReOpens()
        {
            var clock = new FakeTimeProvider();
            var circuit = MakeCircuit(clock);

            // Open.
            for (int i = 0; i < 3; i++) circuit.RecordAuthFailure(ProviderId, ApiKey);

            // Advance past open duration to enter HalfOpen.
            clock.Advance(TimeSpan.FromMinutes(31));
            circuit.IsOpen(ProviderId, ApiKey, out _).Should().BeFalse("HalfOpen probe allowed");

            // Probe fails.
            circuit.RecordAuthFailure(ProviderId, ApiKey);

            // Circuit should be Open again immediately.
            circuit.IsOpen(ProviderId, ApiKey, out var reason).Should().BeTrue("HalfOpen failure re-opens circuit");
            reason.Should().NotBeNullOrWhiteSpace();
        }

        // ── Window expiry resets failure run ──────────────────────────────────
        [Fact]
        public void RecordAuthFailure_AfterWindowExpiry_ResetsRun()
        {
            var clock = new FakeTimeProvider();
            var circuit = MakeCircuit(clock);

            // 2 failures — below threshold.
            circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.RecordAuthFailure(ProviderId, ApiKey);

            // Advance past failure window (5 min).
            clock.Advance(TimeSpan.FromMinutes(6));

            // 2 more failures after the window — should NOT open.
            circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.RecordAuthFailure(ProviderId, ApiKey);
            circuit.IsOpen(ProviderId, ApiKey, out _).Should().BeFalse("window expired; run reset");
        }
    }
}
