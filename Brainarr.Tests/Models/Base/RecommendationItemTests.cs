using Xunit;
using Brainarr.Plugin.Models.Base;

namespace Brainarr.Tests.Models.Base
{
    public class RecommendationItemTests
    {
        [Fact]
        public void IsValid_ReturnsTrueWhenArtistAndAlbumPresent()
        {
            var item = new RecommendationItem
            {
                Artist = "Pink Floyd",
                Album = "The Wall"
            };

            Assert.True(item.IsValid());
        }

        [Theory]
        [InlineData(null, "Album")]
        [InlineData("", "Album")]
        [InlineData(" ", "Album")]
        [InlineData("Artist", null)]
        [InlineData("Artist", "")]
        [InlineData("Artist", " ")]
        public void IsValid_ReturnsFalseWhenMissingRequiredFields(string artist, string album)
        {
            var item = new RecommendationItem
            {
                Artist = artist,
                Album = album
            };

            Assert.False(item.IsValid());
        }

        [Theory]
        [InlineData(0.5, 0.5)]
        [InlineData(0.0, 0.0)]
        [InlineData(1.0, 1.0)]
        [InlineData(1.5, 1.0)]
        [InlineData(-0.5, 0.0)]
        [InlineData(null, 0.5)]
        public void GetNormalizedConfidence_ReturnsValueInRange(double? input, double expected)
        {
            var item = new RecommendationItem { Confidence = input };
            
            Assert.Equal(expected, item.GetNormalizedConfidence());
        }

        [Fact]
        public void RecommendationItem_SerializesCorrectly()
        {
            var item = new RecommendationItem
            {
                Artist = "Radiohead",
                Album = "OK Computer",
                Genre = "Alternative Rock",
                Year = 1997,
                Reason = "Classic album",
                Confidence = 0.95
            };

            var json = System.Text.Json.JsonSerializer.Serialize(item);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<RecommendationItem>(json);

            Assert.Equal(item.Artist, deserialized.Artist);
            Assert.Equal(item.Album, deserialized.Album);
            Assert.Equal(item.Genre, deserialized.Genre);
            Assert.Equal(item.Year, deserialized.Year);
            Assert.Equal(item.Confidence, deserialized.Confidence);
        }
    }
}