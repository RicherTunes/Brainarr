using System;
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
