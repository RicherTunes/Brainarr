using System;
using System.Collections.Generic;
using System.Linq;
using Brainarr.Tests.TestData;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Highly specific unit tests for RecommendationCache focusing on individual cache operations.
    /// This addresses the tech lead's feedback by testing single cache behaviors rather than
    /// complex cache scenarios, making tests more maintainable and focused.
    /// </summary>
    [Trait("Component", "Cache")]
    public class RecommendationCacheSpecificTests
    {
        private readonly RecommendationCache _cache;
        private readonly Logger _logger;

        public RecommendationCacheSpecificTests()
        {
            _logger = LogManager.GetLogger("test");
            _cache = new RecommendationCache(_logger);
        }

        [Fact]
        public void TryGet_WithNonexistentKey_ReturnsFalseAndNullResult()
        {
            // Arrange
            const string nonexistentKey = "key-that-does-not-exist";

            // Act
            var success = _cache.TryGet(nonexistentKey, out var result);

            // Assert - Very specific behavior check
            Assert.False(success);
            Assert.Null(result);
        }

        [Fact]
        public void Set_WithValidData_StoresSuccessfully()
        {
            // Arrange - Using Bogus for realistic test data
            const string key = "valid-data-key";
            var data = TestDataGenerators.ImportListItemGenerator.Generate(3);

            // Act
            _cache.Set(key, data);

            // Assert - Verify exact storage behavior
            var success = _cache.TryGet(key, out var result);
            Assert.True(success);
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);

            // Verify data integrity
            for (int i = 0; i < data.Count; i++)
            {
                Assert.Equal(data[i].Artist, result[i].Artist);
                Assert.Equal(data[i].Album, result[i].Album);
                Assert.Equal(data[i].ReleaseDate, result[i].ReleaseDate);
            }
        }

        [Fact]
        public void Set_WithEmptyList_StoresEmptyList()
        {
            // Arrange
            const string key = "empty-list-key";
            var emptyData = new List<ImportListItemInfo>();

            // Act
            _cache.Set(key, emptyData);

            // Assert - Verify empty list is stored (not null)
            var success = _cache.TryGet(key, out var result);
            Assert.True(success);
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void Set_WithNullData_DoesNotStore()
        {
            // Arrange
            const string key = "null-data-key";

            // Act
            _cache.Set(key, null);

            // Assert - Verify null data is NOT stored
            var success = _cache.TryGet(key, out var result);
            Assert.False(success);
            Assert.Null(result);
        }

        [Fact]
        public void Set_WithCustomDuration_RespectsExpiryTime()
        {
            // Arrange
            const string key = "custom-duration-key";
            var data = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Artist" } };
            var shortDuration = TimeSpan.FromMilliseconds(100);

            // Act
            _cache.Set(key, data, shortDuration);

            // Assert - Immediately retrievable
            var immediateSuccess = _cache.TryGet(key, out var immediateResult);
            Assert.True(immediateSuccess);
            Assert.NotNull(immediateResult);

            // Wait for expiry and verify it's gone
            System.Threading.Thread.Sleep(150);
            var expiredSuccess = _cache.TryGet(key, out var expiredResult);
            Assert.False(expiredSuccess);
            Assert.Null(expiredResult);
        }

        [Fact]
        public void Clear_RemovesAllCachedData()
        {
            // Arrange
            var data = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Artist" } };
            _cache.Set("key1", data);
            _cache.Set("key2", data);

            // Act
            _cache.Clear();

            // Assert - All data removed
            Assert.False(_cache.TryGet("key1", out _));
            Assert.False(_cache.TryGet("key2", out _));
        }

        [Fact]
        public void GenerateCacheKey_WithSameInputs_ProducesSameKey()
        {
            // Arrange
            const string provider = "OpenAI";
            const int maxRecs = 10;
            const string fingerprint = "library-fingerprint-123";

            // Act
            var key1 = _cache.GenerateCacheKey(provider, maxRecs, fingerprint);
            var key2 = _cache.GenerateCacheKey(provider, maxRecs, fingerprint);

            // Assert - Deterministic key generation
            Assert.Equal(key1, key2);
            Assert.NotNull(key1);
            Assert.NotEmpty(key1);
        }

        [Fact]
        public void GenerateCacheKey_WithDifferentInputs_ProducesDifferentKeys()
        {
            // Arrange & Act
            var key1 = _cache.GenerateCacheKey("OpenAI", 10, "fingerprint1");
            var key2 = _cache.GenerateCacheKey("Anthropic", 10, "fingerprint1");
            var key3 = _cache.GenerateCacheKey("OpenAI", 20, "fingerprint1");
            var key4 = _cache.GenerateCacheKey("OpenAI", 10, "fingerprint2");

            // Assert - Each variation produces different key
            Assert.NotEqual(key1, key2); // Different provider
            Assert.NotEqual(key1, key3); // Different max recommendations
            Assert.NotEqual(key1, key4); // Different fingerprint
        }

        [Theory]
        [InlineData(5)]
        [InlineData(50)]
        [InlineData(100)]
        public void Set_WithVariableSizeData_HandlesCorrectly(int itemCount)
        {
            // Arrange - Generate realistic data sets of varying sizes
            var key = $"variable-size-{itemCount}";
            var data = TestDataGenerators.ImportListItemGenerator.Generate(itemCount);

            // Act
            _cache.Set(key, data);

            // Assert - Verify scalability
            var success = _cache.TryGet(key, out var result);
            Assert.True(success);
            Assert.Equal(itemCount, result.Count);

            // Verify random sample for integrity (more efficient than checking all)
            if (itemCount > 0)
            {
                var randomIndex = new Random().Next(0, itemCount);
                Assert.Equal(data[randomIndex].Artist, result[randomIndex].Artist);
                Assert.Equal(data[randomIndex].Album, result[randomIndex].Album);
            }
        }

        [Fact]
        public void Cache_WithRealisticMusicData_MaintainsDataIntegrity()
        {
            // Arrange - Use realistic music data to test real-world scenarios
            var realWorldData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road", ReleaseDate = new DateTime(1969, 9, 26) },
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Dark Side of the Moon", ReleaseDate = new DateTime(1973, 3, 1) },
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "Led Zeppelin IV", ReleaseDate = new DateTime(1971, 11, 8) }
            };

            // Act
            _cache.Set("classic-rock", realWorldData);

            // Assert - Verify real-world data handling
            var success = _cache.TryGet("classic-rock", out var result);
            Assert.True(success);
            Assert.Equal(3, result.Count);

            var beatlesAlbum = result.First(r => r.Artist == "The Beatles");
            Assert.Equal("Abbey Road", beatlesAlbum.Album);
            Assert.Equal(new DateTime(1969, 9, 26), beatlesAlbum.ReleaseDate);
        }
    }
}
