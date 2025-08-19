using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using NLog;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class RecommendationCacheTests
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly Logger _logger;
        private readonly RecommendationCache _cache;

        public RecommendationCacheTests()
        {
            _loggerMock = new Mock<Logger>();
            _logger = _loggerMock.Object;
            _cache = new RecommendationCache(_logger, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void TryGet_WithNonExistentKey_ReturnsFalse()
        {
            // Arrange
            var cacheKey = "non-existent-key";

            // Act
            var result = _cache.TryGet(cacheKey, out var recommendations);

            // Assert
            result.Should().BeFalse();
            recommendations.Should().BeNull();
        }

        [Fact]
        public void TryGet_WithExistingKey_ReturnsTrue()
        {
            // Arrange
            var cacheKey = "test-key";
            var testData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Test Artist", Album = "Test Album" }
            };
            _cache.Set(cacheKey, testData);

            // Act
            var result = _cache.TryGet(cacheKey, out var recommendations);

            // Assert
            result.Should().BeTrue();
            recommendations.Should().HaveCount(1);
            recommendations[0].Artist.Should().Be("Test Artist");
            recommendations[0].Album.Should().Be("Test Album");
        }

        [Fact]
        public async Task Set_WithCustomDuration_ExpiresAfterDuration()
        {
            // Arrange
            var cacheKey = "expiring-key";
            var testData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Test", Album = "Album" }
            };

            // Act
            _cache.Set(cacheKey, testData, TimeSpan.FromMilliseconds(100));
            
            // Assert - Should exist immediately
            _cache.TryGet(cacheKey, out var immediate).Should().BeTrue();
            
            // Wait for expiration
            await Task.Delay(150);
            
            // Should no longer exist
            _cache.TryGet(cacheKey, out var expired).Should().BeFalse();
        }

        [Fact]
        public void Clear_RemovesAllEntries()
        {
            // Arrange
            _cache.Set("key1", new List<ImportListItemInfo> { new ImportListItemInfo() });
            _cache.Set("key2", new List<ImportListItemInfo> { new ImportListItemInfo() });
            _cache.Set("key3", new List<ImportListItemInfo> { new ImportListItemInfo() });

            // Act
            _cache.Clear();

            // Assert
            _cache.TryGet("key1", out _).Should().BeFalse();
            _cache.TryGet("key2", out _).Should().BeFalse();
            _cache.TryGet("key3", out _).Should().BeFalse();
        }

        [Fact]
        public void GenerateCacheKey_WithSameInputs_ProducesSameKey()
        {
            // Arrange
            var provider = "Ollama";
            var maxRecommendations = 20;
            var libraryFingerprint = "1000_5000";

            // Act
            var key1 = _cache.GenerateCacheKey(provider, maxRecommendations, libraryFingerprint);
            var key2 = _cache.GenerateCacheKey(provider, maxRecommendations, libraryFingerprint);

            // Assert
            key1.Should().Be(key2);
        }

        [Fact]
        public void GenerateCacheKey_WithDifferentInputs_ProducesDifferentKeys()
        {
            // Arrange & Act
            var key1 = _cache.GenerateCacheKey("Ollama", 20, "1000_5000");
            var key2 = _cache.GenerateCacheKey("LMStudio", 20, "1000_5000");
            var key3 = _cache.GenerateCacheKey("Ollama", 30, "1000_5000");
            var key4 = _cache.GenerateCacheKey("Ollama", 20, "2000_6000");

            // Assert
            key1.Should().NotBe(key2);
            key1.Should().NotBe(key3);
            key1.Should().NotBe(key4);
            key2.Should().NotBe(key3);
            key2.Should().NotBe(key4);
            key3.Should().NotBe(key4);
        }

        [Fact]
        public void Set_WithNullData_DoesNotThrow()
        {
            // Arrange
            var cacheKey = "null-data-key";

            // Act
            var action = () => _cache.Set(cacheKey, null);

            // Assert
            action.Should().NotThrow();
            _cache.TryGet(cacheKey, out var result).Should().BeFalse();
            result.Should().BeNull();
        }

        [Fact]
        public void Set_UpdatesExistingEntry()
        {
            // Arrange
            var cacheKey = "update-key";
            var originalData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Original", Album = "Album" }
            };
            var updatedData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Updated", Album = "Album" }
            };

            // Act
            _cache.Set(cacheKey, originalData);
            _cache.Set(cacheKey, updatedData);

            // Assert
            _cache.TryGet(cacheKey, out var result).Should().BeTrue();
            result[0].Artist.Should().Be("Updated");
        }

        [Fact]
        public void Constructor_WithNullLogger_DoesNotThrow()
        {
            // Act
            var action = () => new RecommendationCache(null, TimeSpan.FromMinutes(1));

            // Assert
            action.Should().NotThrow();
        }

        [Fact]
        public void Set_LogsCacheOperation()
        {
            // Arrange
            var cacheKey = "log-test-key";
            var testData = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Test", Album = "Album" }
            };

            // Act
            _cache.Set(cacheKey, testData);

            // Assert
            _loggerMock.Verify(x => x.Debug(It.Is<string>(s => 
                s.Contains("Caching") && 
                s.Contains("1 recommendations") && 
                s.Contains(cacheKey))), Times.Once);
        }
    }
}