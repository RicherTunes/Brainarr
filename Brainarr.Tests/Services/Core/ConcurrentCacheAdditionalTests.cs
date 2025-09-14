using System;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Core;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class ConcurrentCacheAdditionalTests
    {
        [Fact]
        public async Task GetOrAddAsync_Evicts_LRU_OnCapacity()
        {
            using var cache = new ConcurrentCache<string, int>(maxSize: 2, defaultExpiration: TimeSpan.FromMinutes(5));

            var a = await cache.GetOrAddAsync("a", _ => Task.FromResult(1));
            var b = await cache.GetOrAddAsync("b", _ => Task.FromResult(2));
            a.Should().Be(1);
            b.Should().Be(2);

            // Insert third via async path to exercise EnsureCapacityAsync LRU eviction
            var c = await cache.GetOrAddAsync("c", _ => Task.FromResult(3));
            c.Should().Be(3);

            cache.TryGet("a", out _).Should().BeFalse("'a' should be evicted as least-recently-used");
            cache.TryGet("b", out var bVal).Should().BeTrue(); bVal.Should().Be(2);
            cache.TryGet("c", out var cVal).Should().BeTrue(); cVal.Should().Be(3);
        }

        [Fact]
        public void Remove_ReturnsFalse_When_KeyMissing()
        {
            using var cache = new ConcurrentCache<string, string>(maxSize: 2, defaultExpiration: TimeSpan.FromMinutes(5));
            cache.Remove("not-there").Should().BeFalse();
        }

        [Fact]
        public void Set_Overwrite_Does_Not_Change_Size()
        {
            using var cache = new ConcurrentCache<string, string>(maxSize: 10, defaultExpiration: TimeSpan.FromMinutes(5));
            cache.Set("k", "v1");
            cache.Set("k", "v2");
            var stats = cache.GetStatistics();
            stats.Size.Should().Be(1);
            cache.TryGet("k", out var v).Should().BeTrue();
            v.Should().Be("v2");
        }

        [Fact]
        public void Clear_Resets_Size_To_Zero()
        {
            using var cache = new ConcurrentCache<string, string>(maxSize: 10, defaultExpiration: TimeSpan.FromMinutes(5));
            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Clear();
            var stats = cache.GetStatistics();
            stats.Size.Should().Be(0);
        }
    }
}
