using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Music;
using NzbDrone.Core.Datastore;

namespace Brainarr.Tests.Integration
{
    /// <summary>
    /// Integration test suite for artist-only recommendation functionality.
    /// Tests the complete flow from AI provider returning artist-only recommendations
    /// through validation, filtering, and conversion to ImportListItemInfo objects.
    /// </summary>
    /// <remarks>
    /// This test addresses the user's original issue where 9 artist recommendations
    /// were processed but "Artists added: 0" occurred due to missing album validation.
    /// The fix ensures artist-only recommendations work correctly in Artists mode.
    /// </remarks>
    [Trait("Category", "Integration")]
    public class ArtistOnlyRecommendationTests
    {
        private readonly RecommendationValidator _validator;
        private readonly Logger _logger;

        public ArtistOnlyRecommendationTests()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _validator = new RecommendationValidator(_logger);
        }

        #region Core Artist-Only Recommendation Tests

        [Fact]
        public void ArtistMode_WithArtistOnlyRecommendations_ShouldPassValidation()
        {
            // Arrange - Simulate AI provider returning artist-only recommendations
            var artistOnlyRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Sigur Rós", Album = null, Genre = "Post-rock", Confidence = 0.9f },
                new Recommendation { Artist = "Hammock", Album = "", Genre = "Ambient", Confidence = 0.85f },
                new Recommendation { Artist = "Ólafur Arnalds", Album = "   ", Genre = "Neoclassical", Confidence = 0.8f }
            };

            // Act - Validate with allowArtistOnly=true (Artists mode)
            var validationResult = _validator.ValidateBatch(artistOnlyRecommendations, allowArtistOnly: true);

            // Assert - All should pass validation in artist mode
            Assert.Equal(3, validationResult.TotalCount);
            Assert.Equal(3, validationResult.ValidCount);
            Assert.Equal(0, validationResult.FilteredCount);
            Assert.Equal(100.0, validationResult.PassRate);
        }

        [Fact]
        public void AlbumMode_WithArtistOnlyRecommendations_ShouldFailValidation()
        {
            // Arrange - Same artist-only recommendations
            var artistOnlyRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Sigur Rós", Album = null, Genre = "Post-rock", Confidence = 0.9f },
                new Recommendation { Artist = "Hammock", Album = "", Genre = "Ambient", Confidence = 0.85f },
                new Recommendation { Artist = "Ólafur Arnalds", Album = "   ", Genre = "Neoclassical", Confidence = 0.8f }
            };

            // Act - Validate with allowArtistOnly=false (Albums mode)
            var validationResult = _validator.ValidateBatch(artistOnlyRecommendations, allowArtistOnly: false);

            // Assert - All should fail validation in album mode
            Assert.Equal(3, validationResult.TotalCount);
            Assert.Equal(0, validationResult.ValidCount);
            Assert.Equal(3, validationResult.FilteredCount);
            Assert.Equal(0.0, validationResult.PassRate);

            // Verify they were filtered for missing album data
            Assert.True(validationResult.FilterReasons.ContainsKey("missing_data"));
            Assert.Equal(3, validationResult.FilterReasons["missing_data"]);
        }

        [Fact]
        public void MixedMode_WithBothArtistOnlyAndAlbumSpecific_ShouldFilterCorrectly()
        {
            // Arrange - Mixed recommendations
            var mixedRecommendations = new List<Recommendation>
            {
                // Valid artist-only recommendations
                new Recommendation { Artist = "Sigur Rós", Album = null, Genre = "Post-rock", Confidence = 0.9f },
                new Recommendation { Artist = "Hammock", Album = "", Genre = "Ambient", Confidence = 0.85f },
                // Valid album-specific recommendations
                new Recommendation { Artist = "Max Richter", Album = "Sleep", Genre = "Neoclassical", Confidence = 0.8f },
                new Recommendation { Artist = "Nils Frahm", Album = "All Melody", Genre = "Electronic", Confidence = 0.75f },
                // Invalid recommendations
                new Recommendation { Artist = null, Album = "Some Album", Genre = "Unknown", Confidence = 0.7f },
                new Recommendation { Artist = "Test Artist", Album = "Album (Reimagined)", Genre = "Rock", Confidence = 0.6f }
            };

            // Act - Validate in artist mode (should accept both artist-only and album-specific)
            var artistModeResult = _validator.ValidateBatch(mixedRecommendations, allowArtistOnly: true);

            // Act - Validate in album mode (should accept only album-specific)
            var albumModeResult = _validator.ValidateBatch(mixedRecommendations, allowArtistOnly: false);

            // Assert - Artist mode should pass 4 valid recommendations
            Assert.Equal(6, artistModeResult.TotalCount);
            Assert.Equal(4, artistModeResult.ValidCount);
            Assert.Equal(2, artistModeResult.FilteredCount);

            // Assert - Album mode should pass only 2 album-specific recommendations
            Assert.Equal(6, albumModeResult.TotalCount);
            Assert.Equal(2, albumModeResult.ValidCount);
            Assert.Equal(4, albumModeResult.FilteredCount);
        }

        #endregion

        #region IterativeRecommendationStrategy Filtering Tests

        [Fact]
        public void IterativeStrategy_ArtistMode_ShouldFilterCorrectly()
        {
            // Arrange
            var strategy = new IterativeRecommendationStrategy(_logger, new LibraryAwarePromptBuilder(_logger));
            var existingArtists = new List<Artist>
            {
                CreateMockArtist(1, "Radiohead"),
                CreateMockArtist(2, "Pink Floyd")
            };

            var recommendations = new List<Recommendation>
            {
                // These should be filtered out (already in library)
                new Recommendation { Artist = "Radiohead", Album = null },
                new Recommendation { Artist = "Pink Floyd", Album = "" },
                // These should pass through (new artists)
                new Recommendation { Artist = "Sigur Rós", Album = null },
                new Recommendation { Artist = "Hammock", Album = "" },
                new Recommendation { Artist = "Ólafur Arnalds", Album = "   " },
            };

            // Use reflection to access private filtering method
            var filterMethod = typeof(IterativeRecommendationStrategy).GetMethod("FilterAndTrackDuplicates",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var existingKeys = existingArtists.Select(a => $"artist_{a.Name.ToLowerInvariant()}").ToHashSet();
            var alreadyRecommended = new List<Recommendation>();
            var rejectedAlbums = new HashSet<string>();

            // Act
            var result = filterMethod.Invoke(strategy, new object[]
            {
                recommendations, existingKeys, alreadyRecommended, rejectedAlbums, true
            });

            // Extract results using reflection
            var resultType = result.GetType();
            var uniqueRecs = (List<Recommendation>)resultType.GetField("Item1")?.GetValue(result);
            var duplicates = (List<Recommendation>)resultType.GetField("Item2")?.GetValue(result);

            // Assert - Should filter existing artists but keep new ones
            Assert.Equal(3, uniqueRecs?.Count);
            Assert.Equal(2, duplicates?.Count);
            Assert.Contains(uniqueRecs, r => r.Artist == "Sigur Rós");
            Assert.Contains(uniqueRecs, r => r.Artist == "Hammock");
            Assert.Contains(uniqueRecs, r => r.Artist == "Ólafur Arnalds");
        }

        #endregion

        #region Library Filtering Integration Tests

        [Fact]
        public void LibraryFiltering_WithExistingArtists_ShouldDetectDuplicates()
        {
            // Arrange - Simulate existing library
            var existingArtists = new List<string> { "radiohead", "pink floyd", "the beatles" };

            var recommendations = new List<Recommendation>
            {
                // Duplicates (case-insensitive)
                new Recommendation { Artist = "Radiohead", Album = null },
                new Recommendation { Artist = "PINK FLOYD", Album = "" },
                new Recommendation { Artist = "The Beatles", Album = null },
                // New artists
                new Recommendation { Artist = "Sigur Rós", Album = null },
                new Recommendation { Artist = "Hammock", Album = "" },
            };

            // Act - Simulate the filtering logic from IterativeRecommendationStrategy
            var filtered = recommendations.Where(r =>
                !existingArtists.Contains(r.Artist?.ToLowerInvariant())).ToList();

            // Assert - Should keep only new artists
            Assert.Equal(2, filtered.Count);
            Assert.Contains(filtered, r => r.Artist == "Sigur Rós");
            Assert.Contains(filtered, r => r.Artist == "Hammock");
        }

        #endregion

        #region Regression Tests (User's Original Issue)

        [Fact]
        public void RegressionTest_NineArtistRecommendations_ValidationScenario()
        {
            // Arrange - Simulate the exact scenario from user's issue
            // 9 artist-only recommendations that should validate in Artists mode
            var nineArtistRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Sigur Rós", Album = null, Genre = "Post-rock", Confidence = 0.95f },
                new Recommendation { Artist = "Hammock", Album = "", Genre = "Ambient", Confidence = 0.92f },
                new Recommendation { Artist = "Ólafur Arnalds", Album = null, Genre = "Neoclassical", Confidence = 0.89f },
                new Recommendation { Artist = "Max Richter", Album = "", Genre = "Contemporary Classical", Confidence = 0.87f },
                new Recommendation { Artist = "A Winged Victory for the Sullen", Album = null, Genre = "Drone", Confidence = 0.85f },
                new Recommendation { Artist = "Stars of the Lid", Album = "", Genre = "Ambient", Confidence = 0.83f },
                new Recommendation { Artist = "Tim Hecker", Album = null, Genre = "Electronic", Confidence = 0.81f },
                new Recommendation { Artist = "William Basinski", Album = "", Genre = "Ambient", Confidence = 0.79f },
                new Recommendation { Artist = "Grouper", Album = null, Genre = "Ambient", Confidence = 0.77f }
            };

            // Act - Validate in Artists mode
            var validationResult = _validator.ValidateBatch(nineArtistRecommendations, allowArtistOnly: true);

            // Assert - Should NOT result in 0 artists validated (this was the bug)
            Assert.Equal(9, validationResult.TotalCount);
            Assert.Equal(9, validationResult.ValidCount);
            Assert.Equal(0, validationResult.FilteredCount);
            Assert.Equal(100.0, validationResult.PassRate);

            // Verify all specific artists from the test case are present
            Assert.All(validationResult.ValidRecommendations, r => Assert.NotEmpty(r.Artist));
            Assert.Contains(validationResult.ValidRecommendations, r => r.Artist == "Sigur Rós");
            Assert.Contains(validationResult.ValidRecommendations, r => r.Artist == "Hammock");
            Assert.Contains(validationResult.ValidRecommendations, r => r.Artist == "Ólafur Arnalds");
            Assert.Contains(validationResult.ValidRecommendations, r => r.Artist == "Max Richter");
        }

        [Fact]
        public void RegressionTest_SameRecommendations_InAlbumMode_ShouldFail()
        {
            // Arrange - Same 9 artist-only recommendations
            var nineArtistRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Sigur Rós", Album = null, Genre = "Post-rock", Confidence = 0.95f },
                new Recommendation { Artist = "Hammock", Album = "", Genre = "Ambient", Confidence = 0.92f },
                new Recommendation { Artist = "Ólafur Arnalds", Album = null, Genre = "Neoclassical", Confidence = 0.89f },
                new Recommendation { Artist = "Max Richter", Album = "", Genre = "Contemporary Classical", Confidence = 0.87f },
                new Recommendation { Artist = "A Winged Victory for the Sullen", Album = null, Genre = "Drone", Confidence = 0.85f },
                new Recommendation { Artist = "Stars of the Lid", Album = "", Genre = "Ambient", Confidence = 0.83f },
                new Recommendation { Artist = "Tim Hecker", Album = null, Genre = "Electronic", Confidence = 0.81f },
                new Recommendation { Artist = "William Basinski", Album = "", Genre = "Ambient", Confidence = 0.79f },
                new Recommendation { Artist = "Grouper", Album = null, Genre = "Ambient", Confidence = 0.77f }
            };

            // Act - Validate in Albums mode (strict album requirement)
            var validationResult = _validator.ValidateBatch(nineArtistRecommendations, allowArtistOnly: false);

            // Assert - Should fail all in album mode due to missing album data
            Assert.Equal(9, validationResult.TotalCount);
            Assert.Equal(0, validationResult.ValidCount);
            Assert.Equal(9, validationResult.FilteredCount);
            Assert.Equal(0.0, validationResult.PassRate);

            // All should be filtered for missing album data
            Assert.True(validationResult.FilterReasons.ContainsKey("missing_data"));
            Assert.Equal(9, validationResult.FilterReasons["missing_data"]);
        }

        #endregion

        #region Performance and Edge Cases

        [Fact]
        public void EdgeCase_EmptyArtistNames_ShouldBeFiltered()
        {
            // Arrange
            var edgeCaseRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "", Album = null },
                new Recommendation { Artist = "   ", Album = "" },
                new Recommendation { Artist = null, Album = null },
                new Recommendation { Artist = "Valid Artist", Album = null }, // Should pass
            };

            // Act
            var validationResult = _validator.ValidateBatch(edgeCaseRecommendations, allowArtistOnly: true);

            // Assert - Only the valid artist should pass
            Assert.Equal(4, validationResult.TotalCount);
            Assert.Equal(1, validationResult.ValidCount);
            Assert.Equal(3, validationResult.FilteredCount);
            Assert.Single(validationResult.ValidRecommendations);
            Assert.Equal("Valid Artist", validationResult.ValidRecommendations.First().Artist);
        }

        [Fact]
        public void Performance_LargeArtistDataset_ShouldHandleEfficiently()
        {
            // Arrange - Create a larger dataset of artist-only recommendations
            var largeDataset = new List<Recommendation>();
            for (int i = 0; i < 100; i++)
            {
                largeDataset.Add(new Recommendation
                {
                    Artist = $"Artist {i}",
                    Album = i % 2 == 0 ? null : "", // Mix of null and empty
                    Genre = "Electronic",
                    Confidence = 0.5f + (i % 50) / 100.0f
                });
            }

            // Act
            var startTime = DateTime.UtcNow;
            var validationResult = _validator.ValidateBatch(largeDataset, allowArtistOnly: true);
            var duration = DateTime.UtcNow - startTime;

            // Assert - Should handle large datasets efficiently
            Assert.Equal(100, validationResult.TotalCount);
            Assert.Equal(100, validationResult.ValidCount);
            Assert.Equal(0, validationResult.FilteredCount);
            Assert.True(duration.TotalMilliseconds < 100, $"Validation took {duration.TotalMilliseconds}ms, should be < 100ms");
        }

        #endregion

        #region Helper Methods

        private Artist CreateMockArtist(int id, string name)
        {
            return new Artist
            {
                Id = id,
                Name = name,
                Added = DateTime.UtcNow.AddDays(-30)
            };
        }

        #endregion

        #region Key Functionality Validation

        [Fact]
        public void KeyFunctionality_RecommendationModeDistinction_ShouldWork()
        {
            // Arrange - Test data that shows the key difference
            var testRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Test Artist 1", Album = null, Genre = "Rock", Confidence = 0.9f },
                new Recommendation { Artist = "Test Artist 2", Album = "", Genre = "Jazz", Confidence = 0.8f },
                new Recommendation { Artist = "Test Artist 3", Album = "   ", Genre = "Electronic", Confidence = 0.7f }
            };

            // Act - Test both modes
            var artistModeResult = _validator.ValidateBatch(testRecommendations, allowArtistOnly: true);
            var albumModeResult = _validator.ValidateBatch(testRecommendations, allowArtistOnly: false);

            // Assert - The core functionality that fixes the user's issue
            Assert.True(artistModeResult.ValidCount > 0);
            Assert.Equal(0, albumModeResult.ValidCount);

            // This validates the fix for "9 recommendations processed but Artists added: 0"
            Assert.Equal(3, artistModeResult.ValidCount);
        }

        #endregion
    }
}
