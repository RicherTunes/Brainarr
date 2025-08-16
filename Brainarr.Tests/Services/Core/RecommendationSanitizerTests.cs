using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationSanitizerTests
    {
        private readonly RecommendationSanitizer _sanitizer;
        private readonly Mock<Logger> _loggerMock;

        public RecommendationSanitizerTests()
        {
            _loggerMock = new Mock<Logger>();
            _sanitizer = new RecommendationSanitizer(_loggerMock.Object);
        }

        [Theory]
        [InlineData("'; DROP TABLE artists; --", "")]
        [InlineData("' OR '1'='1", "' OR '1'='1")] // Quotes should be sanitized
        [InlineData("SELECT * FROM users", "SELECT * FROM users")] // SQL keywords removed when in injection pattern
        [InlineData("Normal Artist Name", "Normal Artist Name")]
        public void SanitizeString_RemovesSqlInjection(string input)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().NotContain("DROP TABLE");
            result.Should().NotContain("';");
            result.Should().NotContain("--");
        }

        [Theory]
        [InlineData("<script>alert('XSS')</script>", "")]
        [InlineData("<img src=x onerror=alert('XSS')>", "")]
        [InlineData("<iframe src='evil.com'></iframe>", "")]
        [InlineData("Normal Text", "Normal Text")]
        public void SanitizeString_RemovesXssPatterns(string input)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().NotContain("<script>");
            result.Should().NotContain("<iframe>");
            result.Should().NotContain("onerror=");
        }

        [Theory]
        [InlineData("../../etc/passwd", "etc/passwd")]
        [InlineData("..\\..\\Windows\\System32", "WindowsSystem32")]
        [InlineData("%2e%2e/config", "config")]
        [InlineData("Normal/Path/Name", "Normal/Path/Name")]
        public void SanitizeString_RemovesPathTraversal(string input)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().NotContain("..");
            result.Should().NotContain("%2e%2e");
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
        public void IsValidRecommendation_WithEmptyAlbum_ReturnsFalse()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Artist",
                Album = "",
                Confidence = 0.5
            };

            // Act
            var result = _sanitizer.IsValidRecommendation(recommendation);

            // Assert
            result.Should().BeFalse();
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
        public void SanitizeRecommendations_FiltersInvalidConfidenceValues()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation
                {
                    Artist = "Artist1",
                    Album = "Album1",
                    Confidence = 1.5 // Over max - should be filtered out
                },
                new Recommendation
                {
                    Artist = "Artist2",
                    Album = "Album2",
                    Confidence = -0.5 // Under min - should be filtered out
                },
                new Recommendation
                {
                    Artist = "Artist3",
                    Album = "Album3",
                    Confidence = 0.5 // Valid - should be kept
                }
            };

            // Act
            var result = _sanitizer.SanitizeRecommendations(recommendations);

            // Assert - Only the valid confidence recommendation should remain
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Artist3");
            result[0].Confidence.Should().Be(0.5);
        }

        [Theory]
        [InlineData("Artist's Name", "Artists Name")] // Apostrophe converted to regular quote then removed
        [InlineData("\"Quoted\"", "Quoted")] // Quotes removed
        [InlineData("<b>Bold</b>", "bBold/b")] // Angle brackets removed but tag content remains
        [InlineData("A & B", "A &amp; B")] // Ampersand encoded
        public void SanitizeString_HandlesSpecialCharacters(string input, string expected)
        {
            // Act
            var result = _sanitizer.SanitizeString(input);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void SanitizeRecommendations_FiltersRecommendationsWithMaliciousContent()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation
                {
                    Artist = "<script>alert('XSS')</script>",
                    Album = "Album",
                    Confidence = 0.5
                },
                new Recommendation
                {
                    Artist = "Valid Artist",
                    Album = "Valid Album",
                    Confidence = 0.8
                }
            };

            // Act
            var result = _sanitizer.SanitizeRecommendations(recommendations);

            // Assert - Only valid recommendation should remain (malicious one filtered out)
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Valid Artist");
        }
    }
}