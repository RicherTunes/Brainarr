using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Caching;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Caching
{
    public class EnhancedRecommendationCacheTests : IDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly EnhancedRecommendationCache _cache;

        public EnhancedRecommendationCacheTests()
        {
            _cache = new EnhancedRecommendationCache(_logger);
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }

        [Fact]
        public void Can_instantiate_cache_with_defaults()
        {
            Assert.NotNull(_cache);
        }

        [Fact]
        public async Task SetAsync_and_GetAsync_roundtrip()
        {
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Test Artist", Album = "Test Album" }
            };

            await _cache.SetAsync("test-key", items);
            var result = await _cache.GetAsync("test-key");

            Assert.True(result.Found);
            Assert.Single(result.Value);
            Assert.Equal("Test Artist", result.Value[0].Artist);
        }

        [Fact]
        public async Task TryGetAsync_miss_returns_false()
        {
            var result = await _cache.TryGetAsync("nonexistent");

            Assert.False(result.Found);
        }

        [Fact]
        public async Task TryGetAsync_hit_returns_true_with_value()
        {
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Cached" }
            };

            await _cache.SetAsync("hit-key", items);
            var result = await _cache.TryGetAsync("hit-key");

            Assert.True(result.Found);
            Assert.Equal("Cached", result.Value[0].Artist);
        }

        [Fact]
        public async Task RemoveAsync_clears_entry()
        {
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "ToRemove" }
            };

            await _cache.SetAsync("remove-key", items);
            await _cache.RemoveAsync("remove-key");
            var result = await _cache.GetAsync("remove-key");

            Assert.False(result.Found);
        }

        [Fact]
        public async Task ClearAsync_empties_cache()
        {
            await _cache.SetAsync("a", new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "A" } });
            await _cache.SetAsync("b", new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "B" } });

            await _cache.ClearAsync();

            var resultA = await _cache.GetAsync("a");
            var resultB = await _cache.GetAsync("b");
            Assert.False(resultA.Found);
            Assert.False(resultB.Found);
        }

        [Fact]
        public void GetStatistics_returns_valid_stats()
        {
            var stats = _cache.GetStatistics();

            Assert.NotNull(stats);
            Assert.Equal(0, stats.TotalHits);
            Assert.Equal(0, stats.TotalMisses);
        }

        [Fact]
        public void CacheResult_Miss_factory()
        {
            var miss = CacheResult<string>.Miss();

            Assert.False(miss.Found);
            Assert.Null(miss.Value);
        }

        [Fact]
        public void CacheResult_Hit_factory()
        {
            var hit = CacheResult<string>.Hit("val", CacheLevel.Memory);

            Assert.True(hit.Found);
            Assert.Equal("val", hit.Value);
            Assert.Equal(CacheLevel.Memory, hit.Level);
        }

        [Fact]
        public void CacheResult_Failure_factory()
        {
            var ex = new InvalidOperationException("test");
            var fail = CacheResult<string>.Failure(ex);

            Assert.False(fail.Found);
            Assert.Equal(ex, fail.Error);
        }
    }
}
