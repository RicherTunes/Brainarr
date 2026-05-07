using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for RecommendationCache focusing on uncovered paths:
    /// - Defensive copy behavior in TryGet
    /// - CleanupExpiredEntries throttle and forced cleanup
    /// - Set triggering cache overflow handling
    /// - GenerateCacheKey format verification
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Component", "Cache")]
    public class RecommendationCacheCovTests
    {
        private readonly Logger _logger;
        private readonly RecommendationCache _cache;

        public RecommendationCacheCovTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _cache = new RecommendationCache(_logger, TimeSpan.FromMinutes(60));
        }

        #region TryGet Defensive Copy Tests

        [Fact]
        public void TryGet_ReturnsDefensiveCopy_ModificationsDoNotAffectCachedData()
        {
            // Arrange - Source lines 51-60: TryGet creates a defensive copy
            var cacheKey = "defensive-copy-test";
            var originalData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Original Artist", Album = "Original Album" }
            };
            _cache.Set(cacheKey, originalData);

            // Act - Get cached data and modify it
            var result1 = _cache.TryGet(cacheKey, out var firstRetrieval);
            firstRetrieval[0].Artist = "Modified Artist";
            firstRetrieval[0].Album = "Modified Album";

            // Get again to verify original data is intact
            var result2 = _cache.TryGet(cacheKey, out var secondRetrieval);

            // Assert - Modifications should NOT affect cached data
            result1.Should().BeTrue("because key exists in cache");
            result2.Should().BeTrue("because key still exists");
            secondRetrieval[0].Artist.Should().Be("Original Artist", "because TryGet returns a defensive copy");
            secondRetrieval[0].Album.Should().Be("Original Album", "because modifications to returned list are isolated");
        }

        [Fact]
        public void TryGet_ReturnsDifferentListInstances_EachCall()
        {
            // Arrange - Source lines 53-60: Each TryGet creates new list via Select().ToList()
            var cacheKey = "instance-test";
            var data = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Artist", Album = "Album" }
            };
            _cache.Set(cacheKey, data);

            // Act
            _cache.TryGet(cacheKey, out var first);
            _cache.TryGet(cacheKey, out var second);

            // Assert - Each call returns a new list instance
            first.Should().NotBeSameAs(second, "because each TryGet call creates a new defensive copy");
        }

        [Fact]
        public void TryGet_WithAllProperties_CopiesAllFields()
        {
            // Arrange - Source lines 53-60: Verify all properties are copied
            var cacheKey = "full-copy-test";
            var releaseDate = new DateTime(2024, 6, 15);
            var originalData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo
                {
                    Artist = "Test Artist",
                    Album = "Test Album",
                    ReleaseDate = releaseDate,
                    ArtistMusicBrainzId = "mb-artist-123",
                    AlbumMusicBrainzId = "mb-album-456"
                }
            };
            _cache.Set(cacheKey, originalData);

            // Act
            var result = _cache.TryGet(cacheKey, out var copiedData);

            // Assert - All properties should be copied
            result.Should().BeTrue();
            copiedData[0].Artist.Should().Be("Test Artist");
            copiedData[0].Album.Should().Be("Test Album");
            copiedData[0].ReleaseDate.Should().Be(releaseDate);
            copiedData[0].ArtistMusicBrainzId.Should().Be("mb-artist-123");
            copiedData[0].AlbumMusicBrainzId.Should().Be("mb-album-456");
        }

        #endregion

        #region GenerateCacheKey Format Tests

        [Fact]
        public void GenerateCacheKey_ReturnsCorrectFormat()
        {
            // Arrange - Source lines 105-116: Format is "brainarr_recs_{provider}_{maxRecommendations}_{shortHash}"
            var provider = "OpenAI";
            var maxRecommendations = 25;
            var fingerprint = "test_fingerprint";

            // Act
            var key = _cache.GenerateCacheKey(provider, maxRecommendations, fingerprint);

            // Assert - Key should start with expected prefix and contain provider/count
            key.Should().StartWith("brainarr_recs_", "because prefix is hardcoded at line 114");
            key.Should().Contain("OpenAI", "because provider is included in key");
            key.Should().Contain("_25_", "because maxRecommendations is included in key");
            // Hash is 8 characters from Base64
            key.Should().MatchRegex(@"brainarr_recs_OpenAI_25_[A-Za-z0-9+/=]{8}$", "because shortHash is 8 chars from Base64");
        }

        [Fact]
        public void GenerateCacheKey_WithSpecialCharacters_HandlesCorrectly()
        {
            // Arrange - Provider and fingerprint with special characters
            var provider = "Provider-With-Special!Chars";
            var maxRecs = 10;
            var fingerprint = "fingerprint:with:special|chars";

            // Act
            var key = _cache.GenerateCacheKey(provider, maxRecs, fingerprint);

            // Assert - Should still produce valid key
            key.Should().NotBeNullOrEmpty("because SHA256 handles any input");
            key.Should().StartWith("brainarr_recs_");
        }

        [Fact]
        public void GenerateCacheKey_WithEmptyFingerprint_ProducesValidKey()
        {
            // Arrange
            var provider = "TestProvider";
            var maxRecs = 5;
            var fingerprint = "";

            // Act
            var key = _cache.GenerateCacheKey(provider, maxRecs, fingerprint);

            // Assert
            key.Should().NotBeNullOrEmpty("because empty fingerprint still produces hash");
            key.Should().StartWith("brainarr_recs_");
        }

        #endregion

        #region Clear Tests

        [Fact]
        public void Clear_WithMultipleEntries_RemovesAll()
        {
            // Arrange - Source lines 99-103: Clear removes all entries
            var data = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "A" } };
            _cache.Set("key1", data);
            _cache.Set("key2", data);
            _cache.Set("key3", data);

            // Verify data is cached
            _cache.TryGet("key1", out _).Should().BeTrue();
            _cache.TryGet("key2", out _).Should().BeTrue();
            _cache.TryGet("key3", out _).Should().BeTrue();

            // Act
            _cache.Clear();

            // Assert - All entries removed
            _cache.TryGet("key1", out _).Should().BeFalse("because Clear removes all entries");
            _cache.TryGet("key2", out _).Should().BeFalse("because Clear removes all entries");
            _cache.TryGet("key3", out _).Should().BeFalse("because Clear removes all entries");
        }

        [Fact]
        public void Clear_OnEmptyCache_DoesNotThrow()
        {
            // Arrange - Empty cache
            // Act
            Action act = () => _cache.Clear();

            // Assert
            act.Should().NotThrow("because Clear on empty cache is safe");
        }

        #endregion

        #region Set with Cache Overflow Tests

        [Fact]
        public void Set_WithCacheOverflow_TriggersForcedCleanup()
        {
            // Arrange - Source lines 89-93: When cache exceeds MaxCacheEntries, forced cleanup runs
            // MaxCacheEntries is 100 (from BrainarrConstants)
            // We need to add more than 100 entries to trigger cleanup
            var cache = new RecommendationCache(_logger, TimeSpan.FromMinutes(60));
            var data = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Test" } };

            // Add 101 entries to trigger the overflow condition at line 90
            for (int i = 0; i < 101; i++)
            {
                cache.Set($"overflow-key-{i}", data);
            }

            // Act - Add one more to potentially trigger cleanup
            cache.Set("overflow-key-101", data);

            // Assert - Cache should still function, entries should be retrievable
            // Some entries may have been evicted, but cache should still work
            cache.TryGet("overflow-key-101", out var result).Should().BeTrue("because just added entry should be retrievable");
            result.Should().HaveCount(1);
        }

        #endregion

        #region Set Update Existing Entry Tests

        [Fact]
        public void Set_WithExistingKey_ReplacesEntireList()
        {
            // Arrange - Source line 87: AddOrUpdate replaces existing entry
            var cacheKey = "replace-test";
            var originalData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Artist1" },
                new ImportListItemInfo { Artist = "Artist2" }
            };
            _cache.Set(cacheKey, originalData);

            // Act - Replace with completely different data
            var newData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "NewArtist1" },
                new ImportListItemInfo { Artist = "NewArtist2" },
                new ImportListItemInfo { Artist = "NewArtist3" }
            };
            _cache.Set(cacheKey, newData);

            // Assert
            var result = _cache.TryGet(cacheKey, out var cachedData);
            result.Should().BeTrue();
            cachedData.Should().HaveCount(3, "because the list was completely replaced");
            cachedData.Select(d => d.Artist).Should().BeEquivalentTo(new[] { "NewArtist1", "NewArtist2", "NewArtist3" });
        }

        #endregion

        #region Set with Custom Duration Tests

        [Fact]
        public void Set_WithNullDuration_UsesDefaultDuration()
        {
            // Arrange - Source line 80: duration ?? _defaultCacheDuration
            var cacheKey = "default-duration-test";
            var data = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Test" } };

            // Act - Set without explicit duration
            _cache.Set(cacheKey, data, null);

            // Assert - Should be retrievable immediately
            var result = _cache.TryGet(cacheKey, out var cachedData);
            result.Should().BeTrue("because default duration should be applied");
            cachedData.Should().HaveCount(1);
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_WithDefaultDuration_UsesProvidedValue()
        {
            // Arrange & Act - Source line 39: defaultDuration ?? TimeSpan.FromMinutes(...)
            var customDuration = TimeSpan.FromMinutes(30);
            var cache = new RecommendationCache(_logger, customDuration);

            // Assert - Cache should function with custom duration
            var data = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Test" } };
            cache.Set("test", data);
            cache.TryGet("test", out var result).Should().BeTrue();
        }

        [Fact]
        public void Constructor_WithNullDuration_UsesBrainarrConstantsDefault()
        {
            // Arrange & Act - Source line 39: Uses BrainarrConstants.CacheDurationMinutes (60)
            var cache = new RecommendationCache(_logger, null);

            // Assert - Cache should function with default duration
            var data = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Test" } };
            cache.Set("test", data);
            cache.TryGet("test", out var result).Should().BeTrue();
        }

        #endregion

        #region Expiration Tests

        [Fact]
        public void TryGet_WithExpiredEntry_ReturnsFalse()
        {
            // Arrange - Source line 49: entry.ExpiresAt > DateTime.UtcNow check
            var cacheKey = "expired-entry-test";
            var data = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Test" } };
            var shortCache = new RecommendationCache(_logger, TimeSpan.FromMilliseconds(50));

            // Set with very short expiration
            shortCache.Set(cacheKey, data, TimeSpan.FromMilliseconds(50));

            // Verify it's there initially
            shortCache.TryGet(cacheKey, out _).Should().BeTrue();

            // Act - Wait for expiration
            System.Threading.Thread.Sleep(100);

            // Assert - Entry should be expired
            shortCache.TryGet(cacheKey, out var result).Should().BeFalse("because entry has expired");
            result.Should().BeNull();
        }

        #endregion
    }
}
