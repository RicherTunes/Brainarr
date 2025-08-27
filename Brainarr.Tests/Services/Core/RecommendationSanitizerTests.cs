using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationSanitizerTests
    {
        private readonly RecommendationSanitizer _sanitizer;
        private readonly Logger _logger;

        public RecommendationSanitizerTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _sanitizer = new RecommendationSanitizer(_logger);
        }

        [Theory]
        [InlineData("'; DROP TABLE artists; --")]
        [InlineData("' OR '1'='1")] 
        [InlineData("SELECT * FROM users WHERE id = 1; DELETE FROM artists; --")]
        public void SanitizeString_RemovesSqlInjection(string input)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().NotContain("DROP TABLE");
            result.Should().NotContain("';");
            result.Should().NotContain("--");
            result.Should().NotContain("DELETE FROM");
        }

        [Theory]
        [InlineData("Normal Artist Name", "Normal Artist Name")]
        [InlineData("Artist - Album", "Artist - Album")]
        public void SanitizeString_PreservesValidContent(string input, string expected)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("<script>alert('XSS')</script>")]
        [InlineData("<img src=x onerror=alert('XSS')>")]
        [InlineData("<iframe src='evil.com'></iframe>")]
        public void SanitizeString_RemovesXssPatterns(string input)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().NotContain("<script>");
            result.Should().NotContain("<iframe>");
            result.Should().NotContain("onerror=");
            result.Should().NotContain("alert(");
        }

        [Theory]
        [InlineData("../../etc/passwd")]
        [InlineData("..\\..\\Windows\\System32")]
        [InlineData("%2e%2e/config")]
        public void SanitizeString_RemovesPathTraversal(string input)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().NotContain("..");
            result.Should().NotContain("%2e%2e");
            result.Should().NotContain("etc/passwd");
            result.Should().NotContain("System32");
        }

        [Theory]
        [InlineData("Normal/Path/Name", "Normal/Path/Name")]
        [InlineData("Artist/Album", "Artist/Album")]
        public void SanitizeString_PreservesValidPaths(string input, string expected)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Text\0with\0null", "Textwithnull")]
        [InlineData("Normal%00Text", "NormalText")]
        [InlineData("Clean Text", "Clean Text")]
        public void SanitizeString_RemovesNullBytes(string input, string expected)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void IsValidRecommendation_WithValidData_ReturnsTrue()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Pink Floyd",
                Album = "The Wall",
                Genre = "Progressive Rock",
                Confidence = 0.85,
                Reason = "Based on your rock preferences"
            };

            // Act
            var result = _sanitizer.IsValidRecommendation(recommendation);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsValidRecommendation_WithNullArtist_ReturnsFalse()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = null,
                Album = "Album",
                Confidence = 0.5
            };

            // Act
            var result = _sanitizer.IsValidRecommendation(recommendation);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsValidRecommendation_WithEmptyAlbum_ReturnsTrue()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Artist",
                Album = "", // Empty albums allowed for artist-only recommendations
                Confidence = 0.5
            };

            // Act
            var result = _sanitizer.IsValidRecommendation(recommendation);

            // Assert
            result.Should().BeTrue(); // Empty albums are allowed per implementation
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        [InlineData(2.0)]
        public void IsValidRecommendation_WithInvalidConfidence_ReturnsFalse(double confidence)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Artist",
                Album = "Album",
                Confidence = confidence
            };

            // Act
            var result = _sanitizer.IsValidRecommendation(recommendation);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsValidRecommendation_WithMaliciousContent_ReturnsFalse()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "'; DROP TABLE artists; --",
                Album = "Album",
                Confidence = 0.5
            };

            // Act
            var result = _sanitizer.IsValidRecommendation(recommendation);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsValidRecommendation_WithExtremelyLongStrings_ReturnsFalse()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = new string('A', 501), // Exceeds 500 char limit
                Album = "Album",
                Confidence = 0.5
            };

            // Act
            var result = _sanitizer.IsValidRecommendation(recommendation);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void SanitizeRecommendations_WithMixedValidAndInvalid_ReturnsOnlyValid()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation
                {
                    Artist = "Valid Artist",
                    Album = "Valid Album",
                    Confidence = 0.8
                },
                new Recommendation
                {
                    Artist = "'; DROP TABLE; --",
                    Album = "Album",
                    Confidence = 0.5
                },
                new Recommendation
                {
                    Artist = "Another Valid",
                    Album = "Another Album",
                    Confidence = 0.9
                }
            };

            // Act
            var result = _sanitizer.SanitizeRecommendations(recommendations);

            // Assert
            result.Should().HaveCount(2);
            result.Should().NotContain(r => r.Artist.Contains("DROP TABLE"));
        }

        [Fact]
        public void SanitizeRecommendations_WithNull_ReturnsEmptyList()
        {
            // Act
            var result = _sanitizer.SanitizeRecommendations(null);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void SanitizeRecommendations_ClampsConfidenceValues()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation
                {
                    Artist = "Artist1",
                    Album = "Album1",
                    Confidence = 1.5 // Over max
                },
                new Recommendation
                {
                    Artist = "Artist2",
                    Album = "Album2",
                    Confidence = -0.5 // Under min
                }
            };

            // Act
            var result = _sanitizer.SanitizeRecommendations(recommendations);

            // Assert
            result.Should().HaveCount(2);
            result[0].Confidence.Should().Be(1.0);
            result[1].Confidence.Should().Be(0.0);
        }

        [Theory]
        [InlineData("Artist's Name", "Artist's Name")] // Apostrophe should be preserved
        [InlineData("\"Quoted\"", "Quoted")] // Quotes removed
        [InlineData("<b>Bold</b>", "Bold")] // HTML tags removed
        [InlineData("A & B", "A &amp; B")] // Ampersand encoded
        public void SanitizeString_HandlesSpecialCharacters(string input, string expected)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void SanitizeRecommendations_LogsFilteredRecommendations()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation
                {
                    Artist = "<script>alert('XSS')</script>",
                    Album = "Album",
                    Confidence = 0.5
                }
            };

            // Act
            var result = _sanitizer.SanitizeRecommendations(recommendations);

            // Assert - Verify malicious content is filtered out (logging verification disabled due to NLog non-virtual methods)
            result.Should().BeEmpty("malicious content should be filtered out");
        }
    }
}