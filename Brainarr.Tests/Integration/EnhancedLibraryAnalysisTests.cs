using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Datastore;

namespace Brainarr.Tests.Integration
{
    public class EnhancedLibraryAnalysisTests
    {
        private readonly Mock<IArtistService> _artistService;
        private readonly Mock<IAlbumService> _albumService;
        private readonly Logger _logger;
        private readonly LibraryAnalyzer _analyzer;
        private readonly LibraryAwarePromptBuilder _promptBuilder;

        public EnhancedLibraryAnalysisTests()
        {
            _artistService = new Mock<IArtistService>();
            _albumService = new Mock<IAlbumService>();
            _logger = TestLogger.CreateNullLogger();
            _analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _logger);
            _promptBuilder = new LibraryAwarePromptBuilder(_logger);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void EndToEnd_RichLibraryAnalysis_GeneratesEnhancedPrompt()
        {
            // Arrange - Create a comprehensive music library
            var artists = CreateRichArtistCollection();
            var albums = CreateRichAlbumCollection();
            
            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);
            
            var settings = new BrainarrSettings
            {
                MaxRecommendations = 10,
                DiscoveryMode = DiscoveryMode.Adjacent,
                SamplingStrategy = SamplingStrategy.Comprehensive,
                Provider = AIProvider.OpenAI
            };
            
            // Act - Analyze library and build prompt
            var profile = _analyzer.AnalyzeLibrary();
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);
            
            // Assert - Verify rich metadata extraction
            profile.Metadata.Should().ContainKey("GenreDistribution");
            profile.Metadata.Should().ContainKey("ReleaseDecades");
            profile.Metadata.Should().ContainKey("PreferredEras");
            profile.Metadata.Should().ContainKey("CollectionCompleteness");
            profile.Metadata.Should().ContainKey("AlbumTypes");
            profile.Metadata.Should().ContainKey("DiscoveryTrend");
            
            // Verify enhanced prompt contains metadata
            prompt.Should().Contain("COLLECTION OVERVIEW");
            prompt.Should().Contain("MUSICAL DNA");
            prompt.Should().Contain("COLLECTION PATTERNS");
            
            // Verify genre distribution is calculated
            var genreDistribution = profile.Metadata["GenreDistribution"] as Dictionary<string, double>;
            genreDistribution.Should().NotBeNull();
            genreDistribution.Should().HaveCountGreaterThan(0);
            
            // Verify temporal analysis
            var decades = profile.Metadata["ReleaseDecades"] as List<string>;
            decades.Should().NotBeNull();
            decades.Should().Contain("2010s");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void EndToEnd_SmallLibrary_AdaptsPromptStrategy()
        {
            // Arrange - Small library (under 50 artists)
            var artists = CreateArtistCollection(20);
            var albums = CreateAlbumCollection(50);
            
            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);
            
            var settings = new BrainarrSettings
            {
                MaxRecommendations = 5,
                SamplingStrategy = SamplingStrategy.Minimal,
                Provider = AIProvider.Ollama
            };
            
            // Act
            var profile = _analyzer.AnalyzeLibrary();
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);
            
            // Assert
            profile.Metadata["CollectionSize"].Should().Be("starter");
            profile.Metadata["DiscoveryTrend"].Should().NotBeNull();
            prompt.Should().Contain("starter");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void EndToEnd_GenreSpecializedCollection_IdentifiesPattern()
        {
            // Arrange - Create metal-focused collection
            var artists = new List<Artist>();
            for (int i = 1; i <= 50; i++)
            {
                artists.Add(CreateArtistWithGenre($"MetalBand{i}", "Metal"));
            }
            
            var albums = new List<Album>();
            foreach (var artist in artists)
            {
                albums.Add(CreateAlbumWithGenre($"Album by {artist.Name}", "Metal", artist.Id));
            }
            
            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);
            
            // Act
            var profile = _analyzer.AnalyzeLibrary();
            
            // Assert
            profile.TopGenres.Should().ContainKey("Metal");
            profile.TopGenres["Metal"].Should().BeGreaterThan(40);
            
            var collectionFocus = profile.Metadata["CollectionFocus"].ToString();
            collectionFocus.Should().Contain("specialized");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void EndToEnd_RecentlyExpandingLibrary_DetectsTrend()
        {
            // Arrange - Many recent additions
            var recentDate = DateTime.UtcNow.AddMonths(-2);
            var oldDate = DateTime.UtcNow.AddYears(-3);
            
            var artists = new List<Artist>();
            // 70% recent additions
            for (int i = 1; i <= 70; i++)
            {
                artists.Add(CreateArtist($"RecentArtist{i}", added: recentDate));
            }
            for (int i = 1; i <= 30; i++)
            {
                artists.Add(CreateArtist($"OldArtist{i}", added: oldDate));
            }
            
            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());
            
            // Act
            var profile = _analyzer.AnalyzeLibrary();
            
            // Assert
            profile.Metadata["DiscoveryTrend"].Should().Be("rapidly expanding");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void EndToEnd_HighQualityCollection_CalculatesMetrics()
        {
            // Arrange - Highly monitored, complete collection
            var artists = CreateArtistCollection(50, allMonitored: true);
            var albums = CreateAlbumCollection(200, allMonitored: true);
            
            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);
            
            // Act
            var profile = _analyzer.AnalyzeLibrary();
            
            // Assert
            var monitoredRatio = (double)profile.Metadata["MonitoredRatio"];
            monitoredRatio.Should().Be(1.0);
            
            var completeness = (double)profile.Metadata["CollectionCompleteness"];
            completeness.Should().Be(1.0);
            
            var avgAlbums = (double)profile.Metadata["AverageAlbumsPerArtist"];
            avgAlbums.Should().Be(4.0);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void EndToEnd_MixedAlbumTypes_ExtractsDistribution()
        {
            // Arrange
            var artists = CreateArtistCollection(10);
            var albums = new List<Album>
            {
                CreateAlbum("Album1", albumType: "Album"),
                CreateAlbum("Album2", albumType: "Album"),
                CreateAlbum("Album3", albumType: "Album"),
                CreateAlbum("EP1", albumType: "EP"),
                CreateAlbum("EP2", albumType: "EP"),
                CreateAlbum("Single1", albumType: "Single"),
                CreateAlbum("Live1", albumType: "Live"),
                CreateAlbum("Compilation1", albumType: "Compilation")
            };
            
            // Add secondary types
            albums[6].SecondaryTypes = new List<NzbDrone.Core.Music.SecondaryAlbumType> { NzbDrone.Core.Music.SecondaryAlbumType.Live };
            albums[7].SecondaryTypes = new List<NzbDrone.Core.Music.SecondaryAlbumType> { NzbDrone.Core.Music.SecondaryAlbumType.Compilation };
            
            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);
            
            // Act
            var profile = _analyzer.AnalyzeLibrary();
            
            // Assert
            var albumTypes = profile.Metadata["AlbumTypes"] as Dictionary<string, int>;
            albumTypes.Should().ContainKey("Album");
            albumTypes["Album"].Should().Be(3);
            albumTypes.Should().ContainKey("EP");
            
            var secondaryTypes = profile.Metadata["SecondaryTypes"] as List<string>;
            secondaryTypes.Should().NotBeNull();
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void EndToEnd_TemporalAnalysis_IdentifiesEraPreferences()
        {
            // Arrange - Albums from different decades
            var artists = CreateArtistCollection(10);
            var albums = new List<Album>
            {
                CreateAlbumWithDate("70s Album 1", new DateTime(1975, 1, 1)),
                CreateAlbumWithDate("70s Album 2", new DateTime(1978, 1, 1)),
                CreateAlbumWithDate("80s Album 1", new DateTime(1985, 1, 1)),
                CreateAlbumWithDate("80s Album 2", new DateTime(1987, 1, 1)),
                CreateAlbumWithDate("90s Album", new DateTime(1995, 1, 1)),
                CreateAlbumWithDate("2000s Album", new DateTime(2005, 1, 1)),
                CreateAlbumWithDate("2010s Album", new DateTime(2015, 1, 1)),
                CreateAlbumWithDate("Recent Album", DateTime.UtcNow.AddMonths(-6)),
                CreateAlbumWithDate("New Album", DateTime.UtcNow.AddMonths(-3))
            };
            
            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);
            
            // Act
            var profile = _analyzer.AnalyzeLibrary();
            
            // Assert
            var releaseDecades = profile.Metadata["ReleaseDecades"] as List<string>;
            releaseDecades.Should().Contain("1970s");
            releaseDecades.Should().Contain("1980s");
            
            var preferredEras = profile.Metadata["PreferredEras"] as List<string>;
            preferredEras.Should().Contain("Golden Age");
            
            var newReleaseRatio = (double)profile.Metadata["NewReleaseRatio"];
            newReleaseRatio.Should().BeGreaterThan(0);
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public void EndToEnd_EmptyLibrary_HandlesGracefully()
        {
            // Arrange
            _artistService.Setup(s => s.GetAllArtists()).Returns(new List<Artist>());
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());
            
            var settings = new BrainarrSettings { MaxRecommendations = 5 };
            
            // Act
            var profile = _analyzer.AnalyzeLibrary();
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, new List<Artist>(), new List<Album>(), settings);
            
            // Assert
            profile.Should().NotBeNull();
            profile.TotalArtists.Should().Be(0);
            profile.Metadata["CollectionSize"].Should().Be("starter");
            profile.Metadata["DiscoveryTrend"].Should().Be("new collection");
            prompt.Should().NotBeNullOrEmpty();
        }

        // Helper methods
        private List<Artist> CreateRichArtistCollection()
        {
            var artists = new List<Artist>();
            var genres = new[] { "Rock", "Electronic", "Jazz", "Metal", "Pop", "Classical" };
            var random = new Random(42);
            
            for (int i = 1; i <= 100; i++)
            {
                var genre = genres[random.Next(genres.Length)];
                var added = DateTime.UtcNow.AddDays(-random.Next(1, 1000));
                artists.Add(CreateArtistWithGenre($"Artist{i}", genre, added: added));
            }
            
            return artists;
        }

        private List<Album> CreateRichAlbumCollection()
        {
            var albums = new List<Album>();
            var random = new Random(42);
            var albumTypes = new[] { "Album", "Album", "Album", "EP", "Single", "Live", "Compilation" };
            
            for (int i = 1; i <= 500; i++)
            {
                var album = CreateAlbum($"Album{i}", 
                    albumType: albumTypes[random.Next(albumTypes.Length)],
                    monitored: random.Next(100) > 20); // 80% monitored
                
                // Add release dates
                var year = 1970 + random.Next(55);
                album.ReleaseDate = new DateTime(year, random.Next(1, 13), 1);
                
                // Add some genres
                if (random.Next(100) > 50)
                {
                    album.Genres = new List<string> { "Rock", "Electronic" }.Take(random.Next(1, 3)).ToList();
                }
                
                albums.Add(album);
            }
            
            return albums;
        }

        private List<Artist> CreateArtistCollection(int count, bool allMonitored = false)
        {
            var artists = new List<Artist>();
            for (int i = 1; i <= count; i++)
            {
                artists.Add(CreateArtist($"Artist{i}", monitored: allMonitored || i % 2 == 0));
            }
            return artists;
        }

        private List<Album> CreateAlbumCollection(int count, bool allMonitored = false)
        {
            var albums = new List<Album>();
            for (int i = 1; i <= count; i++)
            {
                albums.Add(CreateAlbum($"Album{i}", monitored: allMonitored || i % 2 == 0));
            }
            return albums;
        }

        private Artist CreateArtist(string name, bool monitored = true, DateTime? added = null)
        {
            var metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = name });
            return new Artist
            {
                Id = new Random().Next(1, 10000),
                Name = name,
                Monitored = monitored,
                Added = added ?? DateTime.UtcNow.AddMonths(-12),
                Metadata = metadata
            };
        }

        private Artist CreateArtistWithGenre(string name, string genre, DateTime? added = null)
        {
            var artist = CreateArtist(name, added: added);
            artist.Metadata.Value.Genres = new List<string> { genre };
            return artist;
        }

        private Album CreateAlbum(string title, bool monitored = true, string albumType = "Album")
        {
            return new Album
            {
                Title = title,
                Monitored = monitored,
                AlbumType = albumType,
                ArtistId = 1,
                Added = DateTime.UtcNow.AddMonths(-6)
            };
        }

        private Album CreateAlbumWithGenre(string title, string genre, int artistId)
        {
            var album = CreateAlbum(title);
            album.ArtistId = artistId;
            album.Genres = new List<string> { genre };
            return album;
        }

        private Album CreateAlbumWithDate(string title, DateTime releaseDate)
        {
            var album = CreateAlbum(title);
            album.ReleaseDate = releaseDate;
            return album;
        }
    }
}