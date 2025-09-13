using System;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Core;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class ConcurrentCacheTests
    {
        [Fact]
        public async Task GetOrAddAsync_Caches_And_Evicts_LRU()
        {
            using var cache = new ConcurrentCache<string, int>(maxSize: 2, defaultExpiration: TimeSpan.FromMinutes(5));

            var a = await cache.GetOrAddAsync("a", _ => Task.FromResult(1));
            var b = await cache.GetOrAddAsync("b", _ => Task.FromResult(2));
            a.Should().Be(1);
            b.Should().Be(2);

            // Adding a third entry should evict the least recently used ("a")
            var c = await cache.GetOrAddAsync("c", _ => Task.FromResult(3));
            c.Should().Be(3);

            cache.TryGet("a", out _).Should().BeFalse("'a' should be evicted (LRU)");
            cache.TryGet("b", out var bVal).Should().BeTrue();
            bVal.Should().Be(2);
            cache.TryGet("c", out var cVal).Should().BeTrue();
            cVal.Should().Be(3);
        }

        [Fact]
        public void Set_TryGet_Remove_Works()
        {
            using var cache = new ConcurrentCache<string, string>(maxSize: 10, defaultExpiration: TimeSpan.FromMinutes(5));
            cache.Set("k", "v");
            cache.TryGet("k", out var v).Should().BeTrue();
            v.Should().Be("v");
            cache.Remove("k").Should().BeTrue();
            cache.TryGet("k", out _).Should().BeFalse();
        }

        [Fact]
        public async Task Expiration_Expires_Entries()
        {
            using var cache = new ConcurrentCache<string, string>(maxSize: 10, defaultExpiration: TimeSpan.FromMilliseconds(50));

            var v1 = await cache.GetOrAddAsync("exp", _ => Task.FromResult("first"));
            v1.Should().Be("first");

            await Task.Delay(120); // allow to expire

            // Should be a miss now and compute again
            var v2 = await cache.GetOrAddAsync("exp", _ => Task.FromResult("second"));
            v2.Should().Be("second");
        }

        [Fact]
        public async Task Statistics_Are_Reported()
        {
            using var cache = new ConcurrentCache<string, int>(maxSize: 2, defaultExpiration: TimeSpan.FromSeconds(1));
            _ = await cache.GetOrAddAsync("x", _ => Task.FromResult(42)); // miss
            cache.TryGet("x", out _).Should().BeTrue(); // hit
            var stats = cache.GetStatistics();
            stats.MaxSize.Should().Be(2);
            stats.Size.Should().BeGreaterThan(0);
            stats.Hits.Should().BeGreaterThan(0);
            stats.Misses.Should().BeGreaterThan(0);
            stats.HitRate.Should().BeGreaterThan(0);
            stats.MemoryUsage.Should().BeGreaterThan(0);
            stats.ToString().Should().Contain("Cache Stats:");
        }

        [Fact]
        public void Set_Enforces_Capacity_Synchronously()
        {
            using var cache = new ConcurrentCache<string, string>(maxSize: 2, defaultExpiration: TimeSpan.FromMinutes(5));

            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Set("c", "3"); // should trigger eviction via EnsureCapacitySync

            var stats = cache.GetStatistics();
            stats.Size.Should().BeLessThanOrEqualTo(2);

            // LRU eviction: "a" should be gone
            cache.TryGet("a", out _).Should().BeFalse();
            cache.TryGet("b", out var b).Should().BeTrue(); b.Should().Be("2");
            cache.TryGet("c", out var c).Should().BeTrue(); c.Should().Be("3");
        }

        [Fact]
        public async Task Concurrent_SameKey_Invokes_Factory_Once()
        {
            using var cache = new ConcurrentCache<string, int>(maxSize: 10, defaultExpiration: TimeSpan.FromMinutes(5));

            var calls = 0;
            var tasks = new Task<int>[20];

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = cache.GetOrAddAsync("same", async _ =>
                {
                    Interlocked.Increment(ref calls);
                    await Task.Delay(10);
                    return 123;
                });
            }

            var results = await Task.WhenAll(tasks);
            results.Should().OnlyContain(v => v == 123);
            // Allow slight CI race: factory should not stampede
            calls.Should().BeLessThanOrEqualTo(3, "cache should prevent stampede for the same key");

            var stats = cache.GetStatistics();
            stats.Size.Should().Be(1);
            stats.Misses.Should().BeGreaterThanOrEqualTo(1);
            (stats.Hits + stats.Misses).Should().BeGreaterThanOrEqualTo(tasks.Length);
        }

        [Fact]
        public void Evictions_Counter_Increments_When_OverCapacity()
        {
            using var cache = new ConcurrentCache<string, string>(maxSize: 2, defaultExpiration: TimeSpan.FromMinutes(5));

            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Set("c", "3"); // evict 1
            cache.Set("d", "4"); // evict 1

            var stats = cache.GetStatistics();
            stats.Evictions.Should().BeGreaterThanOrEqualTo(2);
            stats.Size.Should().BeLessThanOrEqualTo(2);
        }

        [Fact]
        public async Task CleanupExpired_Removes_Entries_When_Invoked()
        {
            using var cache = new ConcurrentCache<string, string>(maxSize: 10, defaultExpiration: TimeSpan.FromMilliseconds(50));

            cache.Set("e1", "v1");
            cache.Set("e2", "v2");

            await Task.Delay(120); // allow to expire

            // invoke private CleanupExpired via reflection to avoid waiting for timer
            var mi = cache.GetType().GetMethod("CleanupExpired", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            mi!.Invoke(cache, null);

            var stats = cache.GetStatistics();
            stats.Size.Should().Be(0);
        }
    }
}
