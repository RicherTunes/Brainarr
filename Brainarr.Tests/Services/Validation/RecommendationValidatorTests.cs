using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using NzbDrone.Core.Music;

namespace Brainarr.Tests.Services.Validation
{
    public class RecommendationValidatorTests
    {
        private readonly Mock<Logger> _logger;
        private readonly Mock<IArtistService> _artistService;
        private readonly Mock<IAlbumService> _albumService;
        private readonly Mock<IHallucinationDetector> _hallucinationDetector;
        private readonly Mock<IAdvancedDuplicateDetector> _duplicateDetector;
        private readonly Mock<IMusicBrainzService> _musicBrainzService;
        private readonly RecommendationValidator _validator;

        public RecommendationValidatorTests()
        {
            _logger = new Mock<Logger>();
            _artistService = new Mock<IArtistService>();
            _albumService = new Mock<IAlbumService>();
            _hallucinationDetector = new Mock<IHallucinationDetector>();
            _duplicateDetector = new Mock<IAdvancedDuplicateDetector>();
            _musicBrainzService = new Mock<IMusicBrainzService>();
            
            _validator = new RecommendationValidator(
                _logger.Object,
                _artistService.Object,
                _albumService.Object,
                _hallucinationDetector.Object,
                _duplicateDetector.Object,
                _musicBrainzService.Object);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_ValidRecommendation_ReturnsHighScore()
        {
            // Arrange
            var recommendation = CreateValidRecommendation();
            SetupMocksForValidRecommendation();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.Score.Should().BeGreaterThan(0.7);
            result.Findings.Should().NotBeEmpty();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_EmptyArtistName_ReturnsInvalid()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "",
                Album = "Valid Album",
                Year = 2020,
                Confidence = 0.8
            };
            SetupMocksForValidRecommendation();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Score.Should().Be(0.0);
            result.Findings.Should().Contain(f => f.CheckType == ValidationCheckType.FormatValidation);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_ImpossibleReleaseDate_DeductsScore()
        {
            // Arrange
            var recommendation = CreateValidRecommendation();
            recommendation.Year = 1800; // Before recorded music
            SetupMocksForValidRecommendation();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.Score.Should().BeLessThan(0.5);
            result.Findings.Should().Contain(f => 
                f.CheckType == ValidationCheckType.ReleaseDateValidation &&
                f.Severity == ValidationSeverity.Critical);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_FutureReleaseDate_DeductsScore()
        {
            // Arrange
            var recommendation = CreateValidRecommendation();
            recommendation.Year = 2030; // Too far in future
            SetupMocksForValidRecommendation();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.Score.Should().BeLessThan(0.5);
            result.Findings.Should().Contain(f => f.CheckType == ValidationCheckType.ReleaseDateValidation);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_DuplicateAlbum_ReturnsInvalid()
        {
            // Arrange
            var recommendation = CreateValidRecommendation();
            SetupMocksForValidRecommendation();
            _duplicateDetector.Setup(d => d.IsAlreadyInLibraryAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync(true);

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Score.Should().Be(0.0);
            result.Findings.Should().Contain(f => 
                f.CheckType == ValidationCheckType.DuplicateDetection &&
                f.Severity == ValidationSeverity.Critical);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_HighHallucinationConfidence_DeductsScore()
        {
            // Arrange
            var recommendation = CreateValidRecommendation();
            SetupMocksForValidRecommendation();
            _hallucinationDetector.Setup(h => h.DetectHallucinationAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync(new HallucinationDetectionResult
                {
                    HallucinationConfidence = 0.9,
                    DetectedPatterns = new List<HallucinationPattern>
                    {
                        new HallucinationPattern
                        {
                            PatternType = HallucinationPatternType.NonExistentArtist,
                            Description = "Artist name follows AI generation pattern",
                            Confidence = 0.9
                        }
                    }
                });

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.Score.Should().BeLessThan(0.7);
            result.Findings.Should().Contain(f => 
                f.CheckType == ValidationCheckType.HallucinationDetection &&
                f.Severity == ValidationSeverity.Critical);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task FilterValidRecommendations_MixedQuality_FiltersCorrectly()
        {
            // Arrange
            var recommendations = new List<Recommendation>
            {
                CreateValidRecommendation("Artist1", "Album1"),
                CreateInvalidRecommendation("", "Album2"), // Empty artist
                CreateValidRecommendation("Artist3", "Album3"),
                CreateInvalidRecommendation("Artist4", "", 1800) // Empty album, bad year
            };
            
            SetupMocksForValidRecommendation();

            // Act
            var validRecommendations = await _validator.FilterValidRecommendationsAsync(recommendations, 0.7);

            // Assert
            validRecommendations.Should().HaveCount(2);
            validRecommendations.Should().Contain(r => r.Artist == "Artist1");
            validRecommendations.Should().Contain(r => r.Artist == "Artist3");
        }

        [Theory]
        [InlineData("Radiohead", "OK Computer (Multiverse Edition)", false)]
        [InlineData("The Beatles", "Abbey Road (What If Version)", false)]
        [InlineData("Pink Floyd", "The Wall", true)]
        [InlineData("Led Zeppelin", "IV", true)]
        [Trait("Category", "Integration")]
        public async Task ValidateRecommendation_DetectsAIHallucinations(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = 1975,
                Confidence = 0.8,
                Genre = "Rock"
            };

            // Setup hallucination detector to detect fake albums
            _hallucinationDetector.Setup(h => h.DetectHallucinationAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync((Recommendation rec) =>
                {
                    var confidence = 0.0;
                    var patterns = new List<HallucinationPattern>();
                    
                    // Detect obvious AI hallucinations
                    if (rec.Album.Contains("Multiverse") || rec.Album.Contains("What If"))
                    {
                        confidence = 0.9;
                        patterns.Add(new HallucinationPattern
                        {
                            PatternType = HallucinationPatternType.NonExistentAlbum,
                            Description = "Album contains AI hallucination indicators",
                            Confidence = 0.9
                        });
                    }
                    
                    return new HallucinationDetectionResult
                    {
                        HallucinationConfidence = confidence,
                        DetectedPatterns = patterns
                    };
                });

            SetupBasicMocks();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.IsValid.Should().Be(expected);
        }

        [Theory]
        [InlineData("Artist", "Album (Remastered Remastered)", 1970, false)]
        [InlineData("Artist", "Album (100th Anniversary Edition)", 2000, false)]
        [InlineData("Artist", "Album (2050 Remaster)", 1975, false)]
        [InlineData("Artist", "Album (Live at Venue 2100)", null, false)]
        [InlineData("Artist", "Album (37th Anniversary Edition)", 1980, false)]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_ValidatesRemasters(string artist, string album, int? year, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = year,
                Confidence = 0.8
            };

            SetupMocksForValidRecommendation();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.IsValid.Should().Be(expected);
        }

        [Theory]
        [InlineData("Artist", "Album (Live) (Studio Recording)", false)]
        [InlineData("Artist", "Album (Demo) (Deluxe Edition)", false)]
        [InlineData("Artist", "Album (Acoustic Electric Mix)", false)]
        [InlineData("Artist", "Regular Album", true)]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_DetectsSuspiciousCombinations(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = 2000,
                Confidence = 0.8
            };

            _hallucinationDetector.Setup(h => h.DetectHallucinationAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync((Recommendation rec) =>
                {
                    var confidence = 0.0;
                    var patterns = new List<HallucinationPattern>();
                    
                    // Detect contradictory album types
                    if ((rec.Album.Contains("Live") && rec.Album.Contains("Studio")) ||
                        (rec.Album.Contains("Demo") && rec.Album.Contains("Deluxe")) ||
                        (rec.Album.Contains("Acoustic") && rec.Album.Contains("Electric")))
                    {
                        confidence = 0.8;
                        patterns.Add(new HallucinationPattern
                        {
                            PatternType = HallucinationPatternType.SuspiciousCombinations,
                            Description = "Contradictory album descriptors",
                            Confidence = 0.8
                        });
                    }
                    
                    return new HallucinationDetectionResult
                    {
                        HallucinationConfidence = confidence,
                        DetectedPatterns = patterns
                    };
                });

            SetupBasicMocks();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.IsValid.Should().Be(expected);
        }

        [Theory]
        [InlineData("The Beatles", "The Beatles Play The Beatles Playing The Beatles", false)]
        [InlineData("Artist", "Artist's Greatest Artist Collection", false)]
        [InlineData("Band", "Band Music by Band", false)]
        [InlineData("Normal Artist", "Normal Album", true)]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_DetectsAIPatterns(string artist, string album, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = 2000,
                Confidence = 0.8
            };

            _hallucinationDetector.Setup(h => h.DetectHallucinationAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync((Recommendation rec) =>
                {
                    var confidence = 0.0;
                    var patterns = new List<HallucinationPattern>();
                    
                    // Detect repetitive/self-referential patterns
                    var text = $"{rec.Artist} {rec.Album}";
                    var words = text.Split(' ');
                    var repetitions = 0;
                    
                    foreach (var word in words)
                    {
                        var count = 0;
                        foreach (var w in words)
                        {
                            if (string.Equals(word, w, StringComparison.OrdinalIgnoreCase))
                                count++;
                        }
                        if (count > 2) repetitions++;
                    }
                    
                    if (repetitions > 0)
                    {
                        confidence = 0.7 + (repetitions * 0.1);
                        patterns.Add(new HallucinationPattern
                        {
                            PatternType = HallucinationPatternType.RepetitiveElements,
                            Description = "Repetitive elements detected",
                            Confidence = confidence
                        });
                    }
                    
                    return new HallucinationDetectionResult
                    {
                        HallucinationConfidence = confidence,
                        DetectedPatterns = patterns
                    };
                });

            SetupBasicMocks();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.IsValid.Should().Be(expected);
        }

        [Theory]
        [InlineData("Artist", "Album (2050 Remaster)", 1975, false)]
        [InlineData("Artist", "Album (Live at Venue 2100)", null, false)]
        [InlineData("Artist", "Album (1960 Digital Remaster)", 1950, false)]
        [InlineData("Artist", "Album (2020 Remaster)", 1980, true)]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_HandlesFutureDates(string artist, string album, int? year, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = year,
                Confidence = 0.8
            };

            _hallucinationDetector.Setup(h => h.DetectHallucinationAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync((Recommendation rec) =>
                {
                    var confidence = 0.0;
                    var patterns = new List<HallucinationPattern>();
                    
                    // Check for future dates in album titles
                    if (rec.Album.Contains("2050") || rec.Album.Contains("2100"))
                    {
                        confidence = 0.9;
                        patterns.Add(new HallucinationPattern
                        {
                            PatternType = HallucinationPatternType.TemporalInconsistencies,
                            Description = "Future dates detected",
                            Confidence = 0.9
                        });
                    }
                    
                    // Check for anachronistic technology references
                    if (rec.Album.Contains("Digital") && rec.Year < 1980)
                    {
                        confidence = 0.8;
                        patterns.Add(new HallucinationPattern
                        {
                            PatternType = HallucinationPatternType.TemporalInconsistencies,
                            Description = "Anachronistic technology reference",
                            Confidence = 0.8
                        });
                    }
                    
                    return new HallucinationDetectionResult
                    {
                        HallucinationConfidence = confidence,
                        DetectedPatterns = patterns
                    };
                });

            SetupBasicMocks();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.IsValid.Should().Be(expected);
        }

        [Theory]
        [InlineData("Artist", "Album (100th Anniversary Edition)", 2000, false)]
        [InlineData("Artist", "Album (50th Anniversary Edition)", 2000, true)]
        [InlineData("Artist", "Album (25th Anniversary Edition)", 2000, true)]
        [InlineData("Artist", "Album (37th Anniversary Edition)", 1980, false)]
        [Trait("Category", "Unit")]
        public async Task ValidateRecommendation_ValidatesAnniversaryEditions(string artist, string album, int year, bool expected)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = year,
                Confidence = 0.8
            };

            _hallucinationDetector.Setup(h => h.DetectHallucinationAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync((Recommendation rec) =>
                {
                    var confidence = 0.0;
                    var patterns = new List<HallucinationPattern>();
                    
                    // Check for impossible anniversary numbers
                    if (rec.Album.Contains("100th Anniversary") || rec.Album.Contains("37th Anniversary"))
                    {
                        confidence = 0.8;
                        patterns.Add(new HallucinationPattern
                        {
                            PatternType = HallucinationPatternType.TemporalInconsistencies,
                            Description = "Impossible anniversary edition",
                            Confidence = 0.8
                        });
                    }
                    
                    return new HallucinationDetectionResult
                    {
                        HallucinationConfidence = confidence,
                        DetectedPatterns = patterns
                    };
                });

            SetupBasicMocks();

            // Act
            var result = await _validator.ValidateRecommendationAsync(recommendation);

            // Assert
            result.IsValid.Should().Be(expected);
        }

        // Helper methods
        private Recommendation CreateValidRecommendation(string artist = "Radiohead", string album = "OK Computer")
        {
            return new Recommendation
            {
                Artist = artist,
                Album = album,
                Genre = "Alternative Rock",
                Year = 1997,
                Confidence = 0.85,
                Reason = "Great progressive rock album that fits your taste"
            };
        }

        private Recommendation CreateInvalidRecommendation(string artist, string album, int? year = null)
        {
            return new Recommendation
            {
                Artist = artist,
                Album = album,
                Genre = "Rock",
                Year = year,
                Confidence = 0.5
            };
        }

        private void SetupMocksForValidRecommendation()
        {
            _duplicateDetector.Setup(d => d.IsAlreadyInLibraryAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync(false);

            _hallucinationDetector.Setup(h => h.DetectHallucinationAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync(new HallucinationDetectionResult
                {
                    HallucinationConfidence = 0.1,
                    DetectedPatterns = new List<HallucinationPattern>()
                });

            _musicBrainzService.Setup(m => m.ValidateArtistAlbumAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
        }

        private void SetupBasicMocks()
        {
            _duplicateDetector.Setup(d => d.IsAlreadyInLibraryAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync(false);

            _musicBrainzService.Setup(m => m.ValidateArtistAlbumAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
        }
    }
}