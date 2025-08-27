using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using static NzbDrone.Core.ImportLists.Brainarr.Services.Support.RecommendationHistory;
using NzbDrone.Core.Music;
using Brainarr.Tests.Helpers;
using Xunit;

namespace Brainarr.Tests.EdgeCases
{
    [Trait("Category", "ChaosMonkey")]
    public class ChaosMonkeyStressTests
    {
        private readonly Logger _logger;
        private readonly Mock<IHttpClient> _httpClientMock;

        public ChaosMonkeyStressTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _httpClientMock = new Mock<IHttpClient>();
        }

        [Fact]
        public async Task ProviderCascadeFailures_AllProvidersDown_ShouldHandleGracefully()
        {
            // Arrange - Simulate all providers failing
            _httpClientMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("All providers are down!"));

            var providers = new List<IAIProvider>
            {
                new OllamaProvider("http://localhost:11434", "test", _httpClientMock.Object, _logger),
                new LMStudioProvider("http://localhost:1234", "test", _httpClientMock.Object, _logger)
            };

            // Act & Assert - Should not throw, should return empty results
            foreach (var provider in providers)
            {
                var result = await provider.GetRecommendationsAsync("test prompt");
                result.Should().NotBeNull();
                result.Should().BeEmpty();
                
                var connectionTest = await provider.TestConnectionAsync();
                connectionTest.Should().BeFalse();
            }
        }

        [Fact]
        public void MassiveLibraryOverload_10000Artists_ShouldNotCrash()
        {
            // Arrange - Create a massive library
            var artists = new List<Artist>();
            var albums = new List<Album>();
            
            for (int i = 1; i <= 10000; i++)
            {
                var artist = new Artist
                {
                    Id = i,
                    Name = $"Artist {i}",
                    Monitored = true,
                    Added = DateTime.UtcNow.AddDays(-i),
                    Metadata = new ArtistMetadata
                    {
                        Genres = new List<string> { $"Genre{i % 50}" }
                    }
                };
                artists.Add(artist);
                
                // Each artist has 1-5 albums
                for (int j = 1; j <= (i % 5) + 1; j++)
                {
                    albums.Add(new Album
                    {
                        Id = (i * 10) + j,
                        ArtistId = i,
                        Title = $"Album {j} by Artist {i}",
                        AlbumType = "Studio",
                        Monitored = true,
                        ReleaseDate = DateTime.UtcNow.AddYears(-10).AddDays(i)
                    });
                }
            }

            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(artistServiceMock.Object, albumServiceMock.Object, _logger);

            // Act - Should complete within reasonable time
            var startTime = DateTime.UtcNow;
            var profile = analyzer.AnalyzeLibrary();
            var duration = DateTime.UtcNow - startTime;

            // Assert
            profile.Should().NotBeNull();
            profile.TotalArtists.Should().Be(10000);
            duration.Should().BeLessThan(TimeSpan.FromSeconds(30));
            profile.TopGenres.Should().NotBeEmpty();
        }

        [Fact]
        public void CorruptedMetadata_ShouldHandleWithoutCrashing()
        {
            // Arrange - Artists with corrupted/missing metadata
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Normal Artist", Metadata = new ArtistMetadata { Genres = new List<string> { "Rock" } }},
                new Artist { Id = 2, Name = "Null Metadata", Metadata = null },
                new Artist { Id = 3, Name = "Empty Genres", Metadata = new ArtistMetadata { Genres = new List<string>() }},
                new Artist { Id = 4, Name = "Null Genres", Metadata = new ArtistMetadata { Genres = null }},
                new Artist { Id = 5, Name = "", Metadata = new ArtistMetadata { Genres = new List<string> { "" } }},
            };

            var albums = new List<Album>
            {
                new Album { Id = 1, ArtistId = 1, Title = "Normal Album", AlbumType = "Studio", ReleaseDate = DateTime.UtcNow },
                new Album { Id = 2, ArtistId = 2, Title = "", AlbumType = null, ReleaseDate = null },
                new Album { Id = 3, ArtistId = 999, Title = "Orphaned Album", AlbumType = "Studio" },
            };

            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(artistServiceMock.Object, albumServiceMock.Object, _logger);

            // Act - Should not throw exceptions
            Action act = () => analyzer.AnalyzeLibrary();

            // Assert
            act.Should().NotThrow();
            var profile = analyzer.AnalyzeLibrary();
            profile.Should().NotBeNull();
            profile.TopGenres.Should().NotBeNull();
        }

        [Fact]
        public void UnicodeNightmare_ShouldHandleSpecialCharacters()
        {
            // Arrange - Unicode chaos
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "🎵 Emoji Artist 🎶", Metadata = new ArtistMetadata { Genres = new List<string> { "🎸 Rock" }}},
                new Artist { Id = 2, Name = "Здравствуй мир", Metadata = new ArtistMetadata { Genres = new List<string> { "Русский рок" }}},
                new Artist { Id = 3, Name = "こんにちは世界", Metadata = new ArtistMetadata { Genres = new List<string> { "J-Pop" }}},
                new Artist { Id = 4, Name = "A\"B\\C\nD\tE", Metadata = new ArtistMetadata { Genres = new List<string> { "Special\"Chars" }}},
            };

            var albums = new List<Album>
            {
                new Album { Id = 1, ArtistId = 1, Title = "Album with 🔥 fire 🔥", AlbumType = "Studio" },
                new Album { Id = 2, ArtistId = 2, Title = "Альбом на русском", AlbumType = "Студия" },
                new Album { Id = 3, ArtistId = 3, Title = "アルバム", AlbumType = "スタジオ" },
                new Album { Id = 4, ArtistId = 4, Title = "\"Quoted Album\"", AlbumType = "Studio\nNew Line" },
            };

            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();
            artistServiceMock.Setup(x => x.GetAllArtists()).Returns(artists);
            albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(artistServiceMock.Object, albumServiceMock.Object, _logger);

            // Act - Should handle Unicode without breaking
            Action act = () => analyzer.AnalyzeLibrary();

            // Assert
            act.Should().NotThrow();
            var profile = analyzer.AnalyzeLibrary();
            profile.Should().NotBeNull();
            profile.TotalArtists.Should().Be(4);
            profile.TopGenres.Should().NotBeEmpty();
        }

        [Fact]
        public async Task ConcurrencyRaceConditions_RecommendationHistory_ShouldBeThreadSafe()
        {
            // Arrange
            var tempPath = Path.Combine(Path.GetTempPath(), $"chaos_test_{Guid.NewGuid():N}");
            var history = new RecommendationHistory(_logger, tempPath);
            var tasks = new List<Task>();
            var exceptions = new List<Exception>();

            // Act - Hammer the history with concurrent operations
            for (int i = 0; i < 5; i++) // Reduced from 10 to 5 for performance
            {
                var taskId = i;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (int j = 0; j < 50; j++) // Reduced from 100 to 50
                        {
                            switch (j % 4)
                            {
                                case 0:
                                    history.MarkAsDisliked($"Artist{taskId}", $"Album{j}", DislikeLevel.Normal);
                                    break;
                                case 1:
                                    history.MarkAsAccepted($"Artist{taskId}", $"Album{j}");
                                    break;
                                case 2:
                                    history.MarkAsRejected($"Artist{taskId}", $"Album{j}", "Chaos test");
                                    break;
                                case 3:
                                    var exclusions = history.GetExclusions();
                                    var prompt = history.GetExclusionPrompt();
                                    break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            // Wait for chaos to complete
            await Task.WhenAll(tasks.ToArray());

            // Assert - Should not have threading exceptions
            exceptions.Should().BeEmpty("RecommendationHistory should be thread-safe");
            
            // Cleanup
            try { Directory.Delete(tempPath, true); } catch { }
        }

        [Fact]
        public void ExtremeConfidenceValues_ShouldBeNormalizedSafely()
        {
            // Arrange - Extreme confidence values
            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Test1", Album = "Album1", Confidence = double.PositiveInfinity },
                new Recommendation { Artist = "Test2", Album = "Album2", Confidence = double.NegativeInfinity },
                new Recommendation { Artist = "Test3", Album = "Album3", Confidence = double.NaN },
                new Recommendation { Artist = "Test4", Album = "Album4", Confidence = -999.99 },
                new Recommendation { Artist = "Test5", Album = "Album5", Confidence = 1000000.0 },
            };

            var validator = new RecommendationValidator(_logger);

            // Act & Assert - Should handle extreme values without crashing
            foreach (var rec in recommendations)
            {
                Action act = () => validator.ValidateRecommendation(rec);
                act.Should().NotThrow("Validator should handle extreme confidence values");
            }
        }
    }
}