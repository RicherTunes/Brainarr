using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class EnhancedLibraryAnalyzerTests
    {
        private readonly Mock<IArtistService> _artistServiceMock;
        private readonly Mock<IAlbumService> _albumServiceMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly LibraryAnalyzer _analyzer;

        public EnhancedLibraryAnalyzerTests()
        {
            _artistServiceMock = new Mock<IArtistService>();
            _albumServiceMock = new Mock<IAlbumService>();
            _loggerMock = new Mock<Logger>();
            _analyzer = new LibraryAnalyzer(_artistServiceMock.Object, _albumServiceMock.Object, _loggerMock.Object);
        }

        [Fact]
        public void AnalyzeLibrary_ShouldCalculateWeightedGenreDistribution()
        {
            // Arrange
            var artists = CreateArtistsWithGenres(new Dictionary<string, int>
            {
                {"Rock", 50},
                {"Electronic", 20}, 
                {"Jazz", 15},
                {"Classical", 10},
                {"Folk", 3}, // Less than 5% to test occasional genre
                {"Other", 2}
            });
            
            var albums = CreateAlbumsForArtists(artists);
            
            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Should().NotBeNull();
            profile.Metadata.Should().ContainKey("GenreDistribution");
            
            var genreDistribution = profile.Metadata["GenreDistribution"] as Dictionary<string, double>;
            genreDistribution.Should().NotBeNull();
            
            // Verify weighted percentages
            genreDistribution["Rock"].Should().Be(50.0);
            genreDistribution["Electronic"].Should().Be(20.0);
            genreDistribution["Jazz"].Should().Be(15.0);
            genreDistribution["Classical"].Should().Be(10.0);
            genreDistribution["Folk"].Should().Be(3.0);
            
            // Verify significance levels
            genreDistribution.Should().ContainKey("Rock_significance");
            genreDistribution["Rock_significance"].Should().Be(3.0); // Core genre (>=30%)
            genreDistribution["Electronic_significance"].Should().Be(2.0); // Major genre (>=15%)
            genreDistribution["Jazz_significance"].Should().Be(2.0); // Major genre (>=15%)
            genreDistribution["Classical_significance"].Should().Be(1.0); // Minor genre (>=5%)
            genreDistribution["Folk_significance"].Should().Be(0.5); // Occasional genre (<5%)
        }

        [Fact]
        public void AnalyzeLibrary_ShouldCalculateGenreDiversityScore()
        {
            // Arrange - High diversity (even distribution)
            var artists = CreateArtistsWithGenres(new Dictionary<string, int>
            {
                {"Rock", 25},
                {"Electronic", 25},
                {"Jazz", 25},
                {"Classical", 25}
            });
            
            var albums = CreateAlbumsForArtists(artists);
            
            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            var genreDistribution = profile.Metadata["GenreDistribution"] as Dictionary<string, double>;
            genreDistribution["genre_diversity_score"].Should().BeGreaterThan(0.9); // High diversity
            genreDistribution["dominant_genre_percentage"].Should().Be(25.0); // No single dominant genre
        }

        [Fact]
        public void AnalyzeLibrary_ShouldDetectCompletionistBehavior()
        {
            // Arrange - User with many albums per artist (completionist)
            var artists = CreateTestArtists(5);
            var albums = new List<Album>();
            
            // Artist 1: 10 albums (completionist)
            albums.AddRange(CreateAlbumsForArtist(artists[0].Id, 10));
            // Artist 2: 8 albums (completionist)
            albums.AddRange(CreateAlbumsForArtist(artists[1].Id, 8));
            // Artist 3: 6 albums (moderate)
            albums.AddRange(CreateAlbumsForArtist(artists[2].Id, 6));
            // Artist 4: 2 albums (casual)
            albums.AddRange(CreateAlbumsForArtist(artists[3].Id, 2));
            // Artist 5: 1 album (casual)
            albums.AddRange(CreateAlbumsForArtist(artists[4].Id, 1));
            
            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Metadata.Should().ContainKey("CollectionStyle");
            profile.Metadata.Should().ContainKey("CompletionistScore");
            
            var collectionStyle = profile.Metadata["CollectionStyle"].ToString();
            var completionistScore = (double)profile.Metadata["CompletionistScore"];
            
            collectionStyle.Should().Contain("Completionist");
            completionistScore.Should().BeGreaterThan(40.0); // 2 out of 5 artists have 5+ albums
        }

        [Fact]
        public void AnalyzeLibrary_ShouldDetectCasualCollectorBehavior()
        {
            // Arrange - User with few albums per artist (casual)
            var artists = CreateTestArtists(5);
            var albums = new List<Album>();
            
            // Most artists have 1-2 albums each
            albums.AddRange(CreateAlbumsForArtist(artists[0].Id, 1));
            albums.AddRange(CreateAlbumsForArtist(artists[1].Id, 2));
            albums.AddRange(CreateAlbumsForArtist(artists[2].Id, 1));
            albums.AddRange(CreateAlbumsForArtist(artists[3].Id, 2));
            albums.AddRange(CreateAlbumsForArtist(artists[4].Id, 1));
            
            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            var collectionStyle = profile.Metadata["CollectionStyle"].ToString();
            collectionStyle.Should().Contain("Casual");
        }

        [Fact]
        public void AnalyzeLibrary_ShouldDetectPreferredAlbumType()
        {
            // Arrange - User prefers studio albums
            var artists = CreateTestArtists(3);
            var albums = new List<Album>
            {
                CreateAlbum(artists[0].Id, "Studio Album 1", "Studio"),
                CreateAlbum(artists[0].Id, "Studio Album 2", "Studio"),
                CreateAlbum(artists[1].Id, "Studio Album 3", "Studio"),
                CreateAlbum(artists[1].Id, "Studio Album 4", "Studio"),
                CreateAlbum(artists[2].Id, "Greatest Hits", "Compilation"),
                CreateAlbum(artists[2].Id, "Live Album", "Live")
            };
            
            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Metadata.Should().ContainKey("PreferredAlbumType");
            profile.Metadata["PreferredAlbumType"].ToString().Should().Be("Studio Albums");
        }

        [Fact]
        public void AnalyzeLibrary_ShouldHandleEmptyLibrary()
        {
            // Arrange
            _artistServiceMock.Setup(x => x.GetAllArtists()).Returns(new List<Artist>());
            _albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(new List<Album>());

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Should().NotBeNull();
            profile.TotalArtists.Should().Be(0);
            profile.TotalAlbums.Should().Be(0);
            profile.Metadata.Should().ContainKey("GenreDistribution");
            
            var genreDistribution = profile.Metadata["GenreDistribution"] as Dictionary<string, double>;
            genreDistribution.Should().BeEmpty();
        }

        // Helper methods
        private List<Artist> CreateArtistsWithGenres(Dictionary<string, int> genreCounts)
        {
            var artists = new List<Artist>();
            var artistId = 1;
            
            foreach (var genreCount in genreCounts)
            {
                for (int i = 0; i < genreCount.Value; i++)
                {
                    artists.Add(new Artist
                    {
                        Id = artistId++,
                        Name = $"Artist {artistId}",
                        Monitored = true,
                        Added = DateTime.UtcNow.AddDays(-30),
                        Metadata = new ArtistMetadata
                        {
                            Genres = new List<string> { genreCount.Key }
                        }
                    });
                }
            }
            
            return artists;
        }

        private List<Artist> CreateTestArtists(int count)
        {
            var artists = new List<Artist>();
            for (int i = 1; i <= count; i++)
            {
                artists.Add(new Artist
                {
                    Id = i,
                    Name = $"Artist {i}",
                    Monitored = true,
                    Added = DateTime.UtcNow.AddDays(-30)
                });
            }
            return artists;
        }

        private List<Album> CreateAlbumsForArtists(List<Artist> artists)
        {
            var albums = new List<Album>();
            foreach (var artist in artists)
            {
                albums.Add(CreateAlbum(artist.Id, $"Album by {artist.Name}", "Studio"));
            }
            return albums;
        }

        private List<Album> CreateAlbumsForArtist(int artistId, int count)
        {
            var albums = new List<Album>();
            for (int i = 1; i <= count; i++)
            {
                albums.Add(CreateAlbum(artistId, $"Album {i}", "Studio"));
            }
            return albums;
        }

        private Album CreateAlbum(int artistId, string title, string albumType = "Studio")
        {
            return new Album
            {
                Id = new Random().Next(1000, 9999),
                ArtistId = artistId,
                Title = title,
                AlbumType = albumType,
                Monitored = true,
                ReleaseDate = DateTime.UtcNow.AddYears(-2),
                Added = DateTime.UtcNow.AddDays(-30)
            };
        }
    }
}