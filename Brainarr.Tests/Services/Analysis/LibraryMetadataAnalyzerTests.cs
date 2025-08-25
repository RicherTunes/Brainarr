using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Analysis;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Analysis
{
    public class LibraryMetadataAnalyzerTests
    {
        private readonly LibraryMetadataAnalyzer _analyzer;
        private readonly Mock<Logger> _mockLogger;

        public LibraryMetadataAnalyzerTests()
        {
            _mockLogger = new Mock<Logger>();
            _analyzer = new LibraryMetadataAnalyzer(_mockLogger.Object);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task AnalyzeGenresAsync_Should_ExtractGenresFromArtists()
        {
            // Arrange
            var artists = CreateTestArtistsWithGenres();
            var albums = new List<Album>();

            // Act
            var result = await _analyzer.AnalyzeGenresAsync(artists, albums);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("Rock", result.GenreCounts.Keys);
            Assert.Contains("Electronic", result.GenreCounts.Keys);
            Assert.Equal(3, result.GenreCounts["Rock"]);
            Assert.Equal(2, result.GenreCounts["Electronic"]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task AnalyzeGenresAsync_Should_ExtractGenresFromAlbums()
        {
            // Arrange
            var artists = new List<Artist>();
            var albums = CreateTestAlbumsWithGenres();

            // Act
            var result = await _analyzer.AnalyzeGenresAsync(artists, albums);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("Jazz", result.GenreCounts.Keys);
            Assert.Contains("Blues", result.GenreCounts.Keys);
            Assert.Equal(2, result.GenreCounts["Jazz"]);
            Assert.Equal(1, result.GenreCounts["Blues"]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task AnalyzeGenresAsync_Should_FallbackToOverviewExtraction_WhenNoDirectGenres()
        {
            // Arrange
            var artists = CreateTestArtistsWithOverviews();
            var albums = new List<Album>();

            // Act
            var result = await _analyzer.AnalyzeGenresAsync(artists, albums);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("Rock", result.GenreCounts.Keys);
            Assert.Contains("Jazz", result.GenreCounts.Keys);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CalculateGenreDistribution_Should_CalculatePercentages()
        {
            // Arrange
            var genres = new Dictionary<string, int>
            {
                { "Rock", 50 },
                { "Jazz", 30 },
                { "Pop", 20 }
            };

            // Act
            var distribution = _analyzer.CalculateGenreDistribution(genres);

            // Assert
            Assert.Equal(50.0, distribution["Rock"]);
            Assert.Equal(30.0, distribution["Jazz"]);
            Assert.Equal(20.0, distribution["Pop"]);
            Assert.Equal(3.0, distribution["Rock_significance"]); // Core genre
            Assert.Equal(2.0, distribution["Jazz_significance"]); // Major genre
            Assert.Equal(2.0, distribution["Pop_significance"]); // Major genre
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task AnalyzeGenresAsync_Should_HandleLargeDatasets_Efficiently()
        {
            // Arrange
            var artists = GenerateLargeArtistSet(1000);
            var albums = GenerateLargeAlbumSet(5000);
            var startTime = DateTime.UtcNow;

            // Act
            var result = await _analyzer.AnalyzeGenresAsync(artists, albums);

            // Assert
            var duration = DateTime.UtcNow - startTime;
            Assert.True(duration.TotalSeconds < 2, $"Analysis took {duration.TotalSeconds} seconds");
            Assert.NotNull(result);
            Assert.True(result.GenreCounts.Count <= 20); // Should limit to top 20
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public async Task AnalyzeGenresAsync_Should_HandleEmptyLibrary()
        {
            // Arrange
            var artists = new List<Artist>();
            var albums = new List<Album>();

            // Act
            var result = await _analyzer.AnalyzeGenresAsync(artists, albums);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.GenreCounts);
            Assert.Empty(result.Distribution);
            Assert.Equal(0, result.DiversityScore);
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public async Task AnalyzeGenresAsync_Should_HandleNullMetadata()
        {
            // Arrange
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Test Artist", Metadata = null }
            };
            var albums = new List<Album>();

            // Act
            var result = await _analyzer.AnalyzeGenresAsync(artists, albums);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.GenreCounts);
        }

        private List<Artist> CreateTestArtistsWithGenres()
        {
            return new List<Artist>
            {
                new Artist
                {
                    Id = 1,
                    Name = "Artist 1",
                    Metadata = new ArtistMetadataValue
                    {
                        Value = new ArtistMetadata
                        {
                            Genres = new List<string> { "Rock", "Alternative" }
                        }
                    }
                },
                new Artist
                {
                    Id = 2,
                    Name = "Artist 2",
                    Metadata = new ArtistMetadataValue
                    {
                        Value = new ArtistMetadata
                        {
                            Genres = new List<string> { "Electronic", "Rock" }
                        }
                    }
                },
                new Artist
                {
                    Id = 3,
                    Name = "Artist 3",
                    Metadata = new ArtistMetadataValue
                    {
                        Value = new ArtistMetadata
                        {
                            Genres = new List<string> { "Rock", "Electronic" }
                        }
                    }
                }
            };
        }

        private List<Album> CreateTestAlbumsWithGenres()
        {
            return new List<Album>
            {
                new Album { Id = 1, Title = "Album 1", Genres = new List<string> { "Jazz", "Fusion" } },
                new Album { Id = 2, Title = "Album 2", Genres = new List<string> { "Jazz", "Blues" } }
            };
        }

        private List<Artist> CreateTestArtistsWithOverviews()
        {
            return new List<Artist>
            {
                new Artist
                {
                    Id = 1,
                    Name = "Artist 1",
                    Metadata = new ArtistMetadataValue
                    {
                        Value = new ArtistMetadata
                        {
                            Overview = "This artist plays rock music with jazz influences"
                        }
                    }
                }
            };
        }

        private List<Artist> GenerateLargeArtistSet(int count)
        {
            var genres = new[] { "Rock", "Pop", "Jazz", "Electronic", "Classical", "Metal", "Hip Hop", "Country" };
            var artists = new List<Artist>();
            var random = new Random();

            for (int i = 0; i < count; i++)
            {
                artists.Add(new Artist
                {
                    Id = i,
                    Name = $"Artist {i}",
                    Metadata = new ArtistMetadataValue
                    {
                        Value = new ArtistMetadata
                        {
                            Genres = genres.OrderBy(x => random.Next()).Take(random.Next(1, 4)).ToList()
                        }
                    }
                });
            }

            return artists;
        }

        private List<Album> GenerateLargeAlbumSet(int count)
        {
            var genres = new[] { "Rock", "Pop", "Jazz", "Electronic", "Classical", "Metal", "Hip Hop", "Country" };
            var albums = new List<Album>();
            var random = new Random();

            for (int i = 0; i < count; i++)
            {
                albums.Add(new Album
                {
                    Id = i,
                    Title = $"Album {i}",
                    ArtistId = random.Next(0, 1000),
                    Genres = genres.OrderBy(x => random.Next()).Take(random.Next(1, 3)).ToList()
                });
            }

            return albums;
        }
    }
}