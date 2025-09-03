using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Detectors;
using Xunit;

namespace Brainarr.Tests.Services.Validation
{
    public class HallucinationDetectorOrchestratorTests
    {
        private readonly Mock<Logger> _mockLogger;
        private readonly HallucinationDetectorOrchestrator _orchestrator;

        public HallucinationDetectorOrchestratorTests()
        {
            _mockLogger = new Mock<Logger>();
            _orchestrator = new HallucinationDetectorOrchestrator(_mockLogger.Object);
        }

        [Fact]
        public async Task DetectHallucinationAsync_Should_Run_All_Active_Detectors()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Test Artist 123",
                Album = "Test Album XYZ",
                Year = 2024,
                Genre = "Rock"
            };

            // Act
            var result = await _orchestrator.DetectHallucinationAsync(recommendation);

            // Assert
            result.Should().NotBeNull();
            result.DetectedPatterns.Should().NotBeNull();
            result.AnalysisTime.Should().BeGreaterThan(TimeSpan.Zero);
        }

        [Fact]
        public async Task Should_Detect_Obvious_Hallucination()
        {
            // Arrange
            var hallucination = new Recommendation
            {
                Artist = "Unknown Artist",
                Album = "[Placeholder Album]",
                Year = 9999,
                Genre = "Test Genre"
            };

            // Act
            var result = await _orchestrator.DetectHallucinationAsync(hallucination);

            // Assert
            result.IsLikelyHallucination.Should().BeTrue();
            result.HallucinationConfidence.Should().BeGreaterThan(0.7);
            result.DetectedPatterns.Should().NotBeEmpty();
        }

        [Fact]
        public async Task Should_Pass_Valid_Recommendation()
        {
            // Arrange
            var validRecommendation = new Recommendation
            {
                Artist = "Pink Floyd",
                Album = "The Dark Side of the Moon",
                Year = 1973,
                Genre = "Progressive Rock"
            };

            // Act
            var result = await _orchestrator.DetectHallucinationAsync(validRecommendation);

            // Assert
            result.IsLikelyHallucination.Should().BeFalse();
            result.HallucinationConfidence.Should().BeLessThan(0.5);
        }

        [Fact]
        public void Should_Register_And_Unregister_Detectors()
        {
            // Arrange
            var mockDetector = new Mock<ISpecificHallucinationDetector>();
            mockDetector.Setup(d => d.PatternType).Returns(HallucinationPatternType.GenreInconsistency);
            mockDetector.Setup(d => d.IsEnabled).Returns(true);
            mockDetector.Setup(d => d.Priority).Returns(50);

            // Act
            _orchestrator.RegisterDetector(mockDetector.Object);
            var detectorsAfterRegister = _orchestrator.GetActiveDetectors();
            
            _orchestrator.UnregisterDetector(HallucinationPatternType.GenreInconsistency);
            var detectorsAfterUnregister = _orchestrator.GetActiveDetectors();

            // Assert
            detectorsAfterRegister.Should().Contain(d => d.PatternType == HallucinationPatternType.GenreInconsistency);
            detectorsAfterUnregister.Should().NotContain(d => d.PatternType == HallucinationPatternType.GenreInconsistency);
        }

        [Fact]
        public async Task Should_Handle_Detector_Timeout()
        {
            // Arrange
            var slowDetector = new Mock<ISpecificHallucinationDetector>();
            slowDetector.Setup(d => d.PatternType).Returns(HallucinationPatternType.MetadataConflict);
            slowDetector.Setup(d => d.IsEnabled).Returns(true);
            slowDetector.Setup(d => d.Priority).Returns(100);
            slowDetector.Setup(d => d.DetectAsync(It.IsAny<Recommendation>()))
                .Returns(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10)); // Exceed timeout
                    return new HallucinationPattern();
                });

            _orchestrator.RegisterDetector(slowDetector.Object);

            var recommendation = new Recommendation
            {
                Artist = "Test",
                Album = "Test"
            };

            // Act
            var result = await _orchestrator.DetectHallucinationAsync(recommendation);

            // Assert
            result.Should().NotBeNull();
            result.AnalysisTime.Should().BeLessThan(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Should_Calculate_Weighted_Confidence()
        {
            // Arrange
            var mockDetector1 = CreateMockDetector(
                HallucinationPatternType.NonExistentArtist,
                0.8,
                true);
                
            var mockDetector2 = CreateMockDetector(
                HallucinationPatternType.ImpossibleReleaseDate,
                0.6,
                false);

            _orchestrator.RegisterDetector(mockDetector1);
            _orchestrator.RegisterDetector(mockDetector2);

            var recommendation = new Recommendation
            {
                Artist = "Test",
                Album = "Test"
            };

            // Act
            var result = await _orchestrator.DetectHallucinationAsync(recommendation);

            // Assert
            result.HallucinationConfidence.Should().BeGreaterThan(0.6);
            result.DetectedPatterns.Should().HaveCount(2);
        }

        [Theory]
        [InlineData("", "", null)]
        [InlineData("  ", "  ", 2024)]
        [InlineData(null, null, null)]
        public async Task Should_Handle_Invalid_Input(string artist, string album, int? year)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = artist,
                Album = album,
                Year = year
            };

            // Act
            var result = await _orchestrator.DetectHallucinationAsync(recommendation);

            // Assert
            result.Should().NotBeNull();
            result.HallucinationConfidence.Should().BeGreaterThanOrEqualTo(0);
            result.HallucinationConfidence.Should().BeLessThanOrEqualTo(1);
        }

        [Fact]
        public async Task Should_Generate_Meaningful_Summary()
        {
            // Arrange
            var hallucination = new Recommendation
            {
                Artist = "XXXXX",
                Album = "99999",
                Year = 1111
            };

            // Act
            var result = await _orchestrator.DetectHallucinationAsync(hallucination);

            // Assert
            result.Summary.Should().NotBeNullOrWhiteSpace();
            result.Summary.Should().Contain("HALLUCINATION");
        }

        private ISpecificHallucinationDetector CreateMockDetector(
            HallucinationPatternType type,
            double confidence,
            bool isConfirmed)
        {
            var mock = new Mock<ISpecificHallucinationDetector>();
            mock.Setup(d => d.PatternType).Returns(type);
            mock.Setup(d => d.IsEnabled).Returns(true);
            mock.Setup(d => d.Priority).Returns(50);
            mock.Setup(d => d.DetectAsync(It.IsAny<Recommendation>()))
                .ReturnsAsync(new HallucinationPattern
                {
                    PatternType = type,
                    Confidence = confidence,
                    IsConfirmedHallucination = isConfirmed,
                    Description = $"Mock detection for {type}"
                });
            
            return mock.Object;
        }
    }

    public class ArtistExistenceDetectorTests
    {
        private readonly Mock<Logger> _mockLogger;
        private readonly ArtistExistenceDetector _detector;

        public ArtistExistenceDetectorTests()
        {
            _mockLogger = new Mock<Logger>();
            _detector = new ArtistExistenceDetector(_mockLogger.Object);
        }

        [Theory]
        [InlineData("Unknown Artist", 0.9)]
        [InlineData("Test Artist", 0.9)]
        [InlineData("[Artist Name]", 0.8)]
        [InlineData("ABCDEFGHIJK", 0.7)]
        [InlineData("Pink Floyd", 0.0)]
        [InlineData("The Beatles", 0.0)]
        public async Task Should_Detect_Suspicious_Artist_Names(string artistName, double minConfidence)
        {
            // Arrange
            var recommendation = new Recommendation { Artist = artistName };

            // Act
            var result = await _detector.DetectAsync(recommendation);

            // Assert
            result.Should().NotBeNull();
            result.Confidence.Should().BeGreaterThanOrEqualTo(minConfidence);
        }

        [Fact]
        public async Task Should_Detect_Repetitive_Names()
        {
            // Arrange
            var recommendation = new Recommendation { Artist = "Test Test Test Test" };

            // Act
            var result = await _detector.DetectAsync(recommendation);

            // Assert
            result.Confidence.Should().BeGreaterThan(0.4);
            result.Description.Should().Contain("repetition");
        }
    }

    public class ReleaseDateValidatorTests
    {
        private readonly Mock<Logger> _mockLogger;
        private readonly ReleaseDateValidator _validator;

        public ReleaseDateValidatorTests()
        {
            _mockLogger = new Mock<Logger>();
            _validator = new ReleaseDateValidator(_mockLogger.Object);
        }

        [Theory]
        [InlineData(1850, 1.0)] // Before recorded music
        [InlineData(2050, 0.95)] // Far future
        [InlineData(9999, 0.95)] // Suspicious year
        [InlineData(1973, 0.0)] // Valid year
        [InlineData(2024, 0.0)] // Current year
        public async Task Should_Validate_Release_Years(int year, double expectedConfidence)
        {
            // Arrange
            var recommendation = new Recommendation { Year = year };

            // Act
            var result = await _validator.DetectAsync(recommendation);

            // Assert
            result.Confidence.Should().BeApproximately(expectedConfidence, 0.1);
        }

        [Fact]
        public async Task Should_Detect_Genre_Timeline_Conflicts()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Genre = "Dubstep",
                Year = 1950 // Dubstep didn't exist
            };

            // Act
            var result = await _validator.DetectAsync(recommendation);

            // Assert
            result.Confidence.Should().BeGreaterThan(0.7);
            result.Description.Should().Contain("unlikely for genre");
        }
    }
}