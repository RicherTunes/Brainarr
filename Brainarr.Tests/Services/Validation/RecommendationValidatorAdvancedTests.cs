
using Xunit;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;

namespace Brainarr.Tests.Services.Validation
{
    public class RecommendationValidatorAdvancedTests
    {
        private readonly RecommendationValidator _validator;

        public RecommendationValidatorAdvancedTests()
        {
            _validator = new RecommendationValidator(LogManager.GetLogger("test"));
        }

        [Theory]
        [InlineData("Artist", "Album (Fan-Curated Edition)", false)]
        [InlineData("Artist", "Album (2028 Anniversary Remaster)", false)] // Assuming current year is < 2027
        [InlineData("Artist", "Album (Acoustic & Electric Version)", false)]
        public void ValidateRecommendation_WithSubtleHallucinations_ReturnsFalse(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation { Artist = artist, Album = album, Year = 2023 };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Sigur Rós", "Ágætis byrjun", true)]
        [InlineData("Björk", "Vespertine", true)]
        [InlineData("Rammstein", "Mutter", true)]
        [InlineData("P!nk", "I'm Not Dead", true)]
        [InlineData("AC/DC", "Back in Black", true)]
        public void ValidateRecommendation_WithInternationalCharacters_ReturnsTrue(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation { Artist = artist, Album = album };

            // Act
            var result = _validator.ValidateRecommendation(recommendation);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ValidateRecommendation_ArtistOnlyMode_WithHallucinatedAlbum_ReturnsFalse()
        {
            // Arrange
            var recommendation = new Recommendation { Artist = "Artist", Album = "Album (AI Imagined Version)" };

            // Act
            var result = _validator.ValidateRecommendation(recommendation, allowArtistOnly: true);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ValidateRecommendation_AlbumMode_WithEmptyAlbum_ReturnsFalse()
        {
            // Arrange
            var recommendation = new Recommendation { Artist = "Artist", Album = " " };

            // Act
            var result = _validator.ValidateRecommendation(recommendation, allowArtistOnly: false);

            // Assert
            Assert.False(result);
        }
    }
}
