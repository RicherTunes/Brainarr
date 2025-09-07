using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Comprehensive test suite for RecommendationValidator ensuring proper filtering
    /// of AI hallucinations while preserving legitimate album releases.
    /// </summary>
    public class RecommendationValidatorTests
    {
        private readonly RecommendationValidator _validator;
        private readonly Logger _logger;

        public RecommendationValidatorTests()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _validator = new RecommendationValidator(_logger);
        }

        #region Basic Validation Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void ValidateRecommendation_WithNullArtist_ReturnsFalse()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = null,
                Album = "Some Album"
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.False(result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ValidateRecommendation_WithEmptyAlbum_ReturnsFalse()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Some Artist",
                Album = ""
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.False(result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ValidateRecommendation_WithValidAlbum_ReturnsTrue()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Radiohead",
                Album = "OK Computer"
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region AI Hallucination Detection Tests

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Sigur Rós", "Ágætis byrjun (Reimagined)", false)] // AI hallucination
        [InlineData("Max Richter", "Sleep (8-hour version)", false)] // AI exaggeration
        [InlineData("The Beatles", "Abbey Road (What If Version)", false)] // Obviously fictional
        [InlineData("Pink Floyd", "The Wall (Director's Cut)", false)] // Film term
        [InlineData("Radiohead", "OK Computer (Multiverse Edition)", false)] // Fictional term
        [InlineData("Miles Davis", "Kind of Blue (AI Version)", false)] // Obviously AI
        [InlineData("The Beatles", "Let It Be (Redux Redux)", false)] // Double redux
        public void ValidateRecommendation_DetectsAIHallucinations(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Legitimate Album Tests

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Nirvana", "MTV Unplugged in New York", true)] // Real live album
        [InlineData("The Beatles", "Abbey Road (Remastered)", true)] // Real remaster
        [InlineData("Pink Floyd", "The Wall (Deluxe Edition)", true)] // Real deluxe edition
        [InlineData("Radiohead", "OK Computer OKNOTOK 1997 2017", true)] // Real anniversary
        [InlineData("Max Richter", "Sleep", true)] // The actual album (not 8-hour version)
        [InlineData("Led Zeppelin", "Physical Graffiti (Deluxe Edition)", true)] // Real deluxe
        [InlineData("The Beatles", "Let It Be (50th Anniversary)", true)] // Real anniversary
        [InlineData("Bob Dylan", "Blood on the Tracks (Original New York Recording)", true)] // Real version
        public void ValidateRecommendation_AllowsLegitimateAlbums(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = album.Contains("50th") ? 1970 : 1975 // Set appropriate year for anniversary validation
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Edge Case Tests

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Artist", "Album (Live at Madison Square Garden 2024)", true)] // Legitimate venue
        [InlineData("Artist", "Album (Live at the Moon)", false)] // Fake venue
        [InlineData("Artist", "Album (Live at Everywhere)", false)] // Impossible venue
        [InlineData("Artist", "Album (Live at Royal Albert Hall)", true)] // Real venue
        [InlineData("Artist", "Album (Live at the Universe)", false)] // Fake venue
        public void ValidateRecommendation_ValidatesLiveVenues(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Artist", "Album (Remastered)", 1970, true)] // Old album, can be remastered
        [InlineData("Artist", "Album (Remastered)", 2023, false)] // Too recent to be remastered
        [InlineData("Artist", "Album (Remastered Remastered)", 1970, false)] // Double remaster is suspicious
        [InlineData("Artist", "Album (2020 Remaster)", 1980, true)] // Specific year remaster of old album
        public void ValidateRecommendation_ValidatesRemasters(string artist, string album, int? year, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = year
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Artist", "Album (25th Anniversary Edition)", 2000, true)] // Math checks out for 2025
        [InlineData("Artist", "Album (50th Anniversary Edition)", 1975, true)] // Math checks out for 2025
        [InlineData("Artist", "Album (37th Anniversary Edition)", 1980, false)] // Unusual anniversary number
        [InlineData("Artist", "Album (100th Anniversary Edition)", 2000, false)] // Math doesn't work
        public void ValidateRecommendation_ValidatesAnniversaryEditions(string artist, string album, int year, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = year
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Suspicious Combination Tests

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Artist", "Album (Live at Venue) (Remastered)", false)] // Live + Remastered
        [InlineData("Artist", "Album (Demo) (Deluxe Edition)", false)] // Demo + Deluxe
        [InlineData("Artist", "Album (Acoustic) (Instrumental)", false)] // Contradictory
        [InlineData("Artist", "Album (Live) (Studio Recording)", false)] // Contradictory
        [InlineData("Artist", "Album (8-hour version) (Radio Edit)", false)] // Contradictory
        public void ValidateRecommendation_DetectsSuspiciousCombinations(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Excessive Description Tests

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Artist", "Album (Deluxe) (Remastered) (Expanded) (Bonus Tracks)", false)] // Too many
        [InlineData("Artist", "Album (Live) (Acoustic) (Unplugged) (Raw)", false)] // Too many
        [InlineData("Artist", "Album (Deluxe Edition) (2020 Remaster)", true)] // Two is okay
        [InlineData("Artist", "Album", true)] // No parentheses is fine
        public void ValidateRecommendation_DetectsExcessiveDescriptions(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region AI Pattern Detection Tests

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("The Beatles", "The Beatles Play The Beatles Playing The Beatles", false)] // Recursive
        [InlineData("Artist", "A Journey Through The Essential Essence of Music", false)] // AI philosophical
        [InlineData("Artist", "The Ultimate Collection of Collections", false)] // Meta description
        [InlineData("Artist", "Meditation on the Deconstructed Reconstructed Sound", false)] // AI verbose
        public void ValidateRecommendation_DetectsAIPatterns(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ValidateRecommendation_RejectsExcessivelyLongTitles()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Artist",
                Album = new string('A', 101) // 101 characters
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region Batch Validation Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void ValidateBatch_ReturnsCorrectStatistics()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Radiohead", Album = "OK Computer" }, // Valid
                new Recommendation { Artist = "Sigur Rós", Album = "Ágætis byrjun (Reimagined)" }, // Invalid
                new Recommendation { Artist = "Max Richter", Album = "Sleep" }, // Valid
                new Recommendation { Artist = "Artist", Album = "Album (8-hour version)" }, // Invalid
                new Recommendation { Artist = "Nirvana", Album = "MTV Unplugged in New York" } // Valid
            };

            // Act
            var result = _validator.ValidateBatch(recommendations);

            // Assert
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(3, result.ValidCount);
            Assert.Equal(2, result.FilteredCount);
            Assert.Equal(60.0, result.PassRate);
            Assert.Contains("fictional_pattern", string.Join(",", result.FilterReasons.Keys));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ValidateBatch_TracksFilterReasons()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = null, Album = "Album" }, // missing_data
                new Recommendation { Artist = "Artist", Album = "Album (Reimagined)" }, // fictional_pattern
                new Recommendation { Artist = "Artist", Album = "Album (8-hour version)" }, // fictional_pattern
                new Recommendation { Artist = "Artist", Album = new string('A', 101) } // ai_generated_pattern
            };

            // Act
            var result = _validator.ValidateBatch(recommendations);

            // Assert
            Assert.Equal(4, result.FilteredCount);
            Assert.Contains("missing_data", result.FilterReasons.Keys);
            Assert.Contains(result.FilterReasons.Keys, k => k.StartsWith("fictional_pattern"));
        }

        #endregion

        #region Real-World Test Cases from User Report

        [Theory]
        [Trait("Category", "Integration")]
        [InlineData("Sigur Rós", "Ágætis byrjun (Reimagined)", false)]
        [InlineData("Hammock", "Everything and Nothing", true)]
        [InlineData("Ólafur Arnalds", "re:member (Live at the Royal Albert Hall)", true)] // Real live album
        [InlineData("Jóhann Jóhannsson", "Orphée (Original Soundtrack)", true)] // Real soundtrack
        [InlineData("A Winged Victory for the Sullen", "The Undivided Self", true)]
        [InlineData("Hammock", "Everything and Nothing (Live in Berlin)", true)] // Could be real
        [InlineData("Ólafur Arnalds", "re:member (Live at the Barbican)", true)] // Real venue
        [InlineData("Max Richter", "Sleep (8-hour version)", false)] // AI hallucination
        [InlineData("Hania Rani", "Esja", true)]
        [InlineData("Julianna Barwick", "Healing Is a Miracle", true)]
        public void ValidateRecommendation_HandlesRealWorldExamples(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Confidence = 0.85
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Performance Tests

        [Fact]
        [Trait("Category", "Performance")]
        public void ValidateBatch_HandlesLargeDatasetEfficiently()
        {
            // Arrange
            var recommendations = new List<Recommendation>();
            for (int i = 0; i < 1000; i++)
            {
                recommendations.Add(new Recommendation
                {
                    Artist = $"Artist {i}",
                    Album = i % 3 == 0 ? $"Album {i} (Reimagined)" : $"Album {i}",
                    Confidence = 0.7 + (i % 30) / 100.0
                });
            }

            // Act
            var startTime = DateTime.Now;
            var result = _validator.ValidateBatch(recommendations);
            var duration = DateTime.Now - startTime;

            // Assert
            Assert.Equal(1000, result.TotalCount);
            Assert.True(duration.TotalSeconds < 1, $"Validation took {duration.TotalSeconds}s, should be < 1s");
            Assert.True(result.ValidCount > 600 && result.ValidCount < 700); // ~667 should be valid
        }

        #endregion

        #region Future-Proofing Tests

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("Artist", "Album (2025 Remaster)", 1975, true)] // Future remaster of old album
        [InlineData("Artist", "Album (2050 Remaster)", 1975, false)] // Too far in future
        [InlineData("Artist", "Album (Live at Venue 2026)", null, true)] // Near future live album
        [InlineData("Artist", "Album (Live at Venue 2100)", null, false)] // Too far in future
        public void ValidateRecommendation_HandlesFutureDates(string artist, string album, int? year, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = year
            };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion
    }
}
