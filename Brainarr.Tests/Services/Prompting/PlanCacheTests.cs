using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Time;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    public class PlanCacheTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        public void ShouldExpireEntries_ByTtl()
        {
            var clock = new TestClock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 8, metrics: metrics, clock: clock);

            var plan = CreatePlan("key-a", "fp-a");
            cache.Set("key-a", plan, TimeSpan.FromMinutes(5));

            Assert.True(cache.TryGet("key-a", out var cached));
            Assert.Equal("key-a", cached.PlanCacheKey);

            clock.Advance(TimeSpan.FromMinutes(6));

            Assert.False(cache.TryGet("key-a", out _));
            Assert.Equal(1, metrics.Count("prompt.plan_cache_evict"));
            Assert.True(metrics.Count("prompt.plan_cache_miss") >= 1);
            Assert.Equal(0, metrics.LastValue("prompt.plan_cache_size"));
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        public void InvalidateByFingerprint_RemovesAllKeys()
        {
            var clock = new TestClock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 8, metrics: metrics, clock: clock);

            var planA = CreatePlan("key-a", "shared-fp");
            var planB = CreatePlan("key-b", "shared-fp");
            cache.Set("key-a", planA, TimeSpan.FromMinutes(5));
            cache.Set("key-b", planB, TimeSpan.FromMinutes(5));

            cache.InvalidateByFingerprint("shared-fp");

            Assert.False(cache.TryGet("key-a", out _));
            Assert.False(cache.TryGet("key-b", out _));
            Assert.Equal(2, metrics.Count("prompt.plan_cache_evict"));
            Assert.Equal(0, metrics.LastValue("prompt.plan_cache_size"));
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        public void TryGet_IsThreadSafe_UnderConcurrentAccess()
        {
            var clock = new TestClock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 16, metrics: metrics, clock: clock);

            cache.Set("key-a", CreatePlan("key-a", "fp-a"), TimeSpan.FromMinutes(5));

            var failures = 0;
            Parallel.For(0, 64, i =>
            {
                if (!cache.TryGet("key-a", out _))
                {
                    Interlocked.Increment(ref failures);
                }
            });

            Assert.Equal(0, failures);
        }


        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        public void TryGet_SlidesExpiration_OnHit()
        {
            var clock = new TestClock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 4, metrics: metrics, clock: clock);

            cache.Set("key", CreatePlan("key", "fp"), TimeSpan.FromMinutes(5));

            clock.Advance(TimeSpan.FromMinutes(4));
            Assert.True(cache.TryGet("key", out _));

            clock.Advance(TimeSpan.FromMinutes(4));
            Assert.True(cache.TryGet("key", out _));

            clock.Advance(TimeSpan.FromMinutes(6));
            Assert.False(cache.TryGet("key", out _));
        }

        public void TryGet_SweepsExpiredEntries_OnInterval()
        {
            var clock = new TestClock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 4, metrics: metrics, clock: clock);

            cache.Set("key", CreatePlan("key", "fp"), TimeSpan.FromMinutes(5));

            for (var i = 0; i < 3; i++)
            {
                Assert.True(cache.TryGet("key", out _));
                clock.Advance(TimeSpan.FromMinutes(1));
            }

            clock.Advance(TimeSpan.FromMinutes(10));

            Assert.False(cache.TryGet("key", out _));
            Assert.True(metrics.Count("prompt.plan_cache_evict") >= 1);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        public void Set_SweepsExpiredEntriesBeforeInsert()
        {
            var clock = new TestClock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 8, metrics: metrics, clock: clock);

            cache.Set("expired", CreatePlan("expired", "fp-expired"), TimeSpan.FromMinutes(1));
            clock.Advance(TimeSpan.FromMinutes(3));

            cache.Set("active", CreatePlan("active", "fp-active"), TimeSpan.FromMinutes(5));

            Assert.False(cache.TryGet("expired", out _));
            Assert.True(cache.TryGet("active", out _));
        }


        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        public void TryGet_RefreshesTtl_OnHit()
        {
            var clock = new TestClock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 4, metrics: metrics, clock: clock);

            cache.Set("key-a", CreatePlan("key-a", "fp-a"), TimeSpan.FromMinutes(5));

            clock.Advance(TimeSpan.FromMinutes(4));
            Assert.True(cache.TryGet("key-a", out _));

            clock.Advance(TimeSpan.FromMinutes(4));
            Assert.True(cache.TryGet("key-a", out _));

            clock.Advance(TimeSpan.FromMinutes(6));
            Assert.False(cache.TryGet("key-a", out _));

            Assert.True(metrics.Count("prompt.plan_cache_hit") >= 2);
            Assert.True(metrics.Count("prompt.plan_cache_miss") >= 1);
        }
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        public void TryGet_ReturnsDeepCloneForMutableCollections()
        {
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 8, metrics: metrics);

            var plan = new PromptPlan(new LibrarySample(), Array.Empty<string>())
            {
                PlanCacheKey = "key",
                LibraryFingerprint = "fingerprint",
                Compression = PromptCompressionState.Empty,
                StyleCoverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["shoegaze"] = 1 },
                MatchedStyleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["shoegaze"] = 2 },
                TrimmedStyles = new[] { "trimmed" },
                InferredStyleSlugs = new[] { "inferred" }
            };

            cache.Set("key", plan, TimeSpan.FromMinutes(5));

            Assert.True(cache.TryGet("key", out var first));
            var coverage = Assert.IsType<Dictionary<string, int>>(first.StyleCoverage);
            coverage["shoegaze"] = 99;
            var matched = Assert.IsType<Dictionary<string, int>>(first.MatchedStyleCounts);
            matched["shoegaze"] = 77;
            var trimmed = Assert.IsType<string[]>(first.TrimmedStyles);
            trimmed[0] = "mutated";
            var inferred = Assert.IsType<string[]>(first.InferredStyleSlugs);
            inferred[0] = "mutated";

            Assert.True(cache.TryGet("key", out var second));
            var secondCoverage = Assert.IsType<Dictionary<string, int>>(second.StyleCoverage);
            Assert.Equal(1, secondCoverage["shoegaze"]);
            var secondMatched = Assert.IsType<Dictionary<string, int>>(second.MatchedStyleCounts);
            Assert.Equal(2, secondMatched["shoegaze"]);
            var secondTrimmed = Assert.IsType<string[]>(second.TrimmedStyles);
            Assert.Equal("trimmed", secondTrimmed[0]);
            var secondInferred = Assert.IsType<string[]>(second.InferredStyleSlugs);
            Assert.Equal("inferred", secondInferred[0]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PlanCache")]
        public void Configure_CanReduceCapacityAndEvictTail()
        {
            var metrics = new RecordingMetrics();
            var cache = new PlanCache(capacity: 20, metrics: metrics);

            for (var i = 1; i <= 20; i++)
            {
                cache.Set($"k{i}", CreatePlan($"k{i}", "fp"), TimeSpan.FromMinutes(5));
            }

            cache.Configure(16);

            Assert.False(cache.TryGet("k1", out _));
            Assert.False(cache.TryGet("k2", out _));
            Assert.False(cache.TryGet("k3", out _));
            Assert.False(cache.TryGet("k4", out _));
            Assert.True(cache.TryGet("k5", out _));
            Assert.True(cache.TryGet("k20", out _));
        }

        private static PromptPlan CreatePlan(string key, string fingerprint)
        {
            var sample = new LibrarySample();
            return new PromptPlan(sample, Array.Empty<string>())
            {
                PlanCacheKey = key,
                LibraryFingerprint = fingerprint,
                Compression = PromptCompressionState.Empty
            };
        }

        private sealed class TestClock : IClock
        {
            private DateTime _utcNow;

            public TestClock(DateTime start)
            {
                _utcNow = start;
            }

            public DateTime UtcNow => _utcNow;

            public void Advance(TimeSpan delta)
            {
                _utcNow = _utcNow.Add(delta);
            }
        }

        private sealed class RecordingMetrics : IMetrics
        {
            private readonly List<(string Name, double Value)> _records = new();

            public void Record(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
            {
                _records.Add((name, value));
            }

            public int Count(string name)
            {
                return _records.Count(r => string.Equals(r.Name, name, StringComparison.Ordinal));
            }

            public double LastValue(string name)
            {
                for (var i = _records.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_records[i].Name, name, StringComparison.Ordinal))
                    {
                        return _records[i].Value;
                    }
                }

                return 0;
            }
        }
    }
}
