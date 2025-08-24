using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class IterativeRecommendationStrategyTests
    {
        private readonly Mock<IAIProvider> _mockProvider;
        private readonly Mock<LibraryAwarePromptBuilder> _mockPromptBuilder;
        private readonly Logger _logger;
        private readonly IterativeRecommendationStrategy _strategy;
        private readonly BrainarrSettings _settings;
        private readonly LibraryProfile _profile;
        private readonly List<Artist> _existingArtists;
        private readonly List<Album> _existingAlbums;

        public IterativeRecommendationStrategyTests()
        {
            _mockProvider = new Mock<IAIProvider>();
            _mockPromptBuilder = new Mock<LibraryAwarePromptBuilder>();
            _logger = LogManager.GetCurrentClassLogger();
            _strategy = new IterativeRecommendationStrategy(_logger, _mockPromptBuilder.Object);
            
            _settings = new BrainarrSettings 
            { 
                MaxRecommendations = 10,
                Provider = AIProvider.Ollama
            };
            
            _profile = new LibraryProfile
            {
                TopGenres = new Dictionary<string, int> { { "Rock", 50 }, { "Metal", 30 } },
                TopArtists = new List<string> { "Metallica", "Iron Maiden" },
                TotalAlbums = 100,
                TotalArtists = 50
            };

            _existingArtists = new List<Artist>
            {
                CreateArtist("Metallica"),
                CreateArtist("Iron Maiden"),
                CreateArtist("Black Sabbath")
            };

            _existingAlbums = new List<Album>
            {
                CreateAlbum("Metallica", "Master of Puppets"),
                CreateAlbum("Iron Maiden", "The Number of the Beast"),
                CreateAlbum("Black Sabbath", "Paranoid")
            };

            // Setup default prompt builder behavior
            _mockPromptBuilder.Setup(x => x.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(),
                It.IsAny<List<Artist>>(),
                It.IsAny<List<Album>>(),
                It.IsAny<BrainarrSettings>(),
                It.IsAny<bool>()))
                .Returns("Test prompt");
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task SingleIterationSuccess_ReturnsEnoughUniqueRecommendations()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller", Year = 1990 },
                new Recommendation { Artist = "Megadeth", Album = "Rust in Peace", Year = 1990 },
                new Recommendation { Artist = "Slayer", Album = "Reign in Blood", Year = 1986 },
                new Recommendation { Artist = "Anthrax", Album = "Among the Living", Year = 1987 },
                new Recommendation { Artist = "Testament", Album = "The Legacy", Year = 1987 },
                new Recommendation { Artist = "Exodus", Album = "Bonded by Blood", Year = 1985 },
                new Recommendation { Artist = "Overkill", Album = "The Years of Decay", Year = 1989 },
                new Recommendation { Artist = "Death", Album = "Leprosy", Year = 1988 },
                new Recommendation { Artist = "Kreator", Album = "Pleasure to Kill", Year = 1986 },
                new Recommendation { Artist = "Sodom", Album = "Agent Orange", Year = 1989 }
            };

            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations);

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Equal(10, result.Count);
            Assert.All(result, r => Assert.NotNull(r.Artist));
            Assert.All(result, r => Assert.NotNull(r.Album));
            _mockProvider.Verify(x => x.GetRecommendationsAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task MultiIterationScenario_HandlesDuplicatesAcrossIterations()
        {
            // Arrange
            var iteration1 = new List<Recommendation>
            {
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" },
                new Recommendation { Artist = "Metallica", Album = "Master of Puppets" }, // Duplicate of existing
                new Recommendation { Artist = "Megadeth", Album = "Rust in Peace" },
                new Recommendation { Artist = "Iron Maiden", Album = "The Number of the Beast" }, // Duplicate of existing
                new Recommendation { Artist = "Slayer", Album = "Reign in Blood" }
            };

            var iteration2 = new List<Recommendation>
            {
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" }, // Duplicate from iteration 1
                new Recommendation { Artist = "Anthrax", Album = "Among the Living" },
                new Recommendation { Artist = "Testament", Album = "The Legacy" },
                new Recommendation { Artist = "Exodus", Album = "Bonded by Blood" },
                new Recommendation { Artist = "Overkill", Album = "The Years of Decay" },
                new Recommendation { Artist = "Death", Album = "Leprosy" }
            };

            var iteration3 = new List<Recommendation>
            {
                new Recommendation { Artist = "Kreator", Album = "Pleasure to Kill" },
                new Recommendation { Artist = "Sodom", Album = "Agent Orange" },
                new Recommendation { Artist = "Destruction", Album = "Eternal Devastation" },
                new Recommendation { Artist = "Venom", Album = "Black Metal" },
                new Recommendation { Artist = "Celtic Frost", Album = "Morbid Tales" }
            };

            var callCount = 0;
            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return callCount switch
                    {
                        1 => iteration1,
                        2 => iteration2,
                        3 => iteration3,
                        _ => new List<Recommendation>()
                    };
                });

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Equal(10, result.Count);
            Assert.Equal(3, callCount); // Should have made 3 iterations
            
            // Verify no duplicates in final result
            var uniqueKeys = result.Select(r => $"{r.Artist}_{r.Album}").Distinct().Count();
            Assert.Equal(result.Count, uniqueKeys);
            
            // Verify no existing albums in result
            Assert.DoesNotContain(result, r => r.Artist == "Metallica" && r.Album == "Master of Puppets");
            Assert.DoesNotContain(result, r => r.Artist == "Iron Maiden" && r.Album == "The Number of the Beast");
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task MaxIterationsReached_StopsAfterThreeAttempts()
        {
            // Arrange - Always return duplicates to force max iterations
            var duplicateRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Metallica", Album = "Master of Puppets" },
                new Recommendation { Artist = "Iron Maiden", Album = "The Number of the Beast" },
                new Recommendation { Artist = "Black Sabbath", Album = "Paranoid" }
            };

            var callCount = 0;
            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return duplicateRecommendations;
                });

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Empty(result); // All recommendations were duplicates
            Assert.Equal(3, callCount); // Should stop after MAX_ITERATIONS (3)
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task LowSuccessRateExit_StopsWhenSuccessRateDrops()
        {
            // Arrange
            var iteration1 = new List<Recommendation>
            {
                // Mix of unique and duplicates for reasonable success rate
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" },
                new Recommendation { Artist = "Megadeth", Album = "Rust in Peace" },
                new Recommendation { Artist = "Slayer", Album = "Reign in Blood" },
                new Recommendation { Artist = "Metallica", Album = "Master of Puppets" }, // Duplicate
                new Recommendation { Artist = "Iron Maiden", Album = "The Number of the Beast" } // Duplicate
            };

            var iteration2 = new List<Recommendation>
            {
                // Mostly duplicates for low success rate
                new Recommendation { Artist = "Metallica", Album = "Master of Puppets" }, // Duplicate
                new Recommendation { Artist = "Iron Maiden", Album = "The Number of the Beast" }, // Duplicate
                new Recommendation { Artist = "Black Sabbath", Album = "Paranoid" }, // Duplicate
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" }, // Duplicate from iteration 1
                new Recommendation { Artist = "Anthrax", Album = "Among the Living" } // Only new one
            };

            var callCount = 0;
            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return callCount switch
                    {
                        1 => iteration1,
                        2 => iteration2,
                        _ => new List<Recommendation>()
                    };
                });

            _settings.MaxRecommendations = 20; // Set high to ensure iterations would normally continue

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Equal(4, result.Count); // 3 from iteration 1, 1 from iteration 2
            Assert.Equal(2, callCount); // Should have made 2 iterations before low success rate exit
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task EmptyProviderResponse_TerminatesGracefully()
        {
            // Arrange
            var iteration1 = new List<Recommendation>
            {
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" },
                new Recommendation { Artist = "Megadeth", Album = "Rust in Peace" }
            };

            var callCount = 0;
            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return callCount switch
                    {
                        1 => iteration1,
                        2 => new List<Recommendation>(), // Empty response
                        _ => new List<Recommendation>()
                    };
                });

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal(2, callCount); // Should stop after empty response
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task ProviderException_HandlesGracefully()
        {
            // Arrange
            var iteration1 = new List<Recommendation>
            {
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" },
                new Recommendation { Artist = "Megadeth", Album = "Rust in Peace" }
            };

            var callCount = 0;
            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                        return iteration1;
                    throw new Exception("Provider error");
                });

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal(2, callCount); // Should stop after exception
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task ArtistOnlyMode_HandlesArtistRecommendations()
        {
            // Arrange
            var artistRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Judas Priest" }, // No album
                new Recommendation { Artist = "Megadeth" },
                new Recommendation { Artist = "Slayer" },
                new Recommendation { Artist = "Anthrax" },
                new Recommendation { Artist = "Testament" },
                new Recommendation { Artist = "Metallica" }, // Duplicate existing artist
                new Recommendation { Artist = "Exodus" },
                new Recommendation { Artist = "Overkill" },
                new Recommendation { Artist = "Death" },
                new Recommendation { Artist = "Kreator" },
                new Recommendation { Artist = "Sodom" }
            };

            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(artistRecommendations);

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                true); // Artist-only mode

            // Assert
            Assert.Equal(10, result.Count);
            Assert.All(result, r => Assert.NotNull(r.Artist));
            Assert.DoesNotContain(result, r => r.Artist == "Metallica"); // Should filter existing artist
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task MixedMode_FiltersAlbumlessRecommendationsInAlbumMode()
        {
            // Arrange
            var mixedRecommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" },
                new Recommendation { Artist = "Megadeth" }, // No album - should be filtered in album mode
                new Recommendation { Artist = "Slayer", Album = "Reign in Blood" },
                new Recommendation { Artist = "Anthrax" }, // No album
                new Recommendation { Artist = "Testament", Album = "The Legacy" }
            };

            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(mixedRecommendations);

            _settings.MaxRecommendations = 3;

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false); // Album mode

            // Assert
            Assert.Equal(3, result.Count);
            Assert.All(result, r => Assert.NotNull(r.Album)); // All should have albums
            Assert.DoesNotContain(result, r => string.IsNullOrWhiteSpace(r.Album));
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task IterativeContext_IncludesRejectedAndExistingInfo()
        {
            // Arrange
            string capturedPrompt = null;
            _mockPromptBuilder.Setup(x => x.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(),
                It.IsAny<List<Artist>>(),
                It.IsAny<List<Album>>(),
                It.IsAny<BrainarrSettings>(),
                It.IsAny<bool>()))
                .Returns("Base prompt");

            var iteration1 = new List<Recommendation>
            {
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" },
                new Recommendation { Artist = "Metallica", Album = "Master of Puppets" }, // Duplicate
                new Recommendation { Artist = "Megadeth", Album = "Rust in Peace" }
            };

            var iteration2 = new List<Recommendation>
            {
                new Recommendation { Artist = "Slayer", Album = "Reign in Blood" },
                new Recommendation { Artist = "Anthrax", Album = "Among the Living" }
            };

            var callCount = 0;
            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .Callback<string>(prompt => 
                {
                    if (callCount == 1) // Capture second iteration prompt
                        capturedPrompt = prompt;
                })
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return callCount switch
                    {
                        1 => iteration1,
                        2 => iteration2,
                        _ => new List<Recommendation>()
                    };
                });

            _settings.MaxRecommendations = 5;

            // Act
            await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.NotNull(capturedPrompt);
            Assert.Contains("ITERATIVE REQUEST CONTEXT", capturedPrompt);
            Assert.Contains("Previously rejected", capturedPrompt);
            Assert.Contains("Already recommended", capturedPrompt);
            Assert.Contains("OPTIMIZATION HINTS", capturedPrompt);
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task RequestSizeCalculation_IncreasesWithIterations()
        {
            // Arrange
            var capturedPrompts = new List<string>();
            
            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .Callback<string>(prompt => capturedPrompts.Add(prompt))
                .ReturnsAsync(() => new List<Recommendation>()); // Always return empty to force iterations

            _settings.MaxRecommendations = 10;

            // Act
            await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Equal(3, capturedPrompts.Count); // MAX_ITERATIONS
            
            // Verify request sizes increase
            Assert.Contains("Requesting 15 recommendations", capturedPrompts[0]); // 10 * 1.5
            Assert.Contains("Requesting 20 recommendations", capturedPrompts[1]); // 10 * 2.0
            Assert.Contains("Requesting 30 recommendations", capturedPrompts[2]); // 10 * 3.0
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task NormalizationLogic_HandlesVariations()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                new Recommendation { Artist = "JUDAS PRIEST", Album = "PAINKILLER" },
                new Recommendation { Artist = "Judas Priest", Album = "Painkiller" }, // Duplicate with different case
                new Recommendation { Artist = "  Judas  Priest  ", Album = "  Painkiller  " }, // Duplicate with spaces
                new Recommendation { Artist = "Megadeth", Album = "Rust in Peace" }
            };

            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(recommendations);

            _settings.MaxRecommendations = 10;

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Equal(2, result.Count); // Should only have 2 unique albums
            Assert.Contains(result, r => r.Artist.Contains("Judas") || r.Artist.Contains("JUDAS"));
            Assert.Contains(result, r => r.Artist == "Megadeth");
        }

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task CompletionRate_DeterminesContinuation()
        {
            // Arrange
            var iteration1 = Enumerable.Range(1, 7)
                .Select(i => new Recommendation { Artist = $"Artist{i}", Album = $"Album{i}" })
                .ToList();

            var iteration2 = Enumerable.Range(8, 3)
                .Select(i => new Recommendation { Artist = $"Artist{i}", Album = $"Album{i}" })
                .ToList();

            var callCount = 0;
            _mockProvider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return callCount switch
                    {
                        1 => iteration1,
                        2 => iteration2,
                        _ => new List<Recommendation>()
                    };
                });

            _settings.MaxRecommendations = 10;

            // Act
            var result = await _strategy.GetIterativeRecommendationsAsync(
                _mockProvider.Object,
                _profile,
                _existingArtists,
                _existingAlbums,
                _settings,
                false);

            // Assert
            Assert.Equal(10, result.Count);
            Assert.Equal(2, callCount); // Should continue because 70% < 80% threshold
        }

        private Artist CreateArtist(string name)
        {
            return new Artist
            {
                Name = name,
                Metadata = new ArtistMetadata { Name = name }
            };
        }

        private Album CreateAlbum(string artistName, string albumTitle)
        {
            return new Album
            {
                Title = albumTitle,
                ArtistMetadata = new ArtistMetadata { Name = artistName }
            };
        }
    }
}