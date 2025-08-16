using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Xunit;
using NLog;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Tests.Services
{
    public class RecommendationStagingTests
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly RecommendationStaging _staging;

        public RecommendationStagingTests()
        {
            _loggerMock = new Mock<Logger>();
            _staging = new RecommendationStaging(_loggerMock.Object);
        }

        [Fact]
        public void StageRecommendation_LowConfidence_IsStaged()
        {
            // Arrange
            var recommendation = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.6,
                DisplayArtist = "Test Artist",
                DisplayAlbum = "Test Album"
            };

            // Act
            _staging.StageRecommendation(recommendation);
            var staged = _staging.GetStagedRecommendations();

            // Assert
            Assert.Single(staged);
            Assert.Equal(recommendation, staged.First());
        }

        [Fact]
        public void StageRecommendation_HighConfidence_NotStaged()
        {
            // Arrange
            var recommendation = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.9,
                DisplayArtist = "High Confidence Artist",
                DisplayAlbum = "High Confidence Album"
            };

            // Act
            _staging.StageRecommendation(recommendation);
            var staged = _staging.GetStagedRecommendations();

            // Assert
            Assert.Empty(staged);
        }

        [Fact]
        public void StageRecommendation_NotResolved_NotStaged()
        {
            // Arrange
            var recommendation = new ResolvedRecommendation
            {
                Status = ResolutionStatus.NotFound,
                Confidence = 0.5,
                DisplayArtist = "Not Found Artist"
            };

            // Act
            _staging.StageRecommendation(recommendation);
            var staged = _staging.GetStagedRecommendations();

            // Assert
            Assert.Empty(staged);
        }

        [Fact]
        public void ProcessStagedRecommendations_FiltersbyConfidence()
        {
            // Arrange
            var lowConfidence = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.4,
                DisplayArtist = "Low"
            };

            var mediumConfidence = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.65,
                DisplayArtist = "Medium"
            };

            var higherConfidence = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.75,
                DisplayArtist = "Higher"
            };

            _staging.StageRecommendation(lowConfidence);
            _staging.StageRecommendation(mediumConfidence);
            _staging.StageRecommendation(higherConfidence);

            // Act
            var processed = _staging.ProcessStagedRecommendations(0.6);

            // Assert
            Assert.Equal(2, processed.Count);
            Assert.Contains(processed, r => r.DisplayArtist == "Medium");
            Assert.Contains(processed, r => r.DisplayArtist == "Higher");
            Assert.DoesNotContain(processed, r => r.DisplayArtist == "Low");

            // Verify low confidence is still staged
            var remaining = _staging.GetStagedRecommendations();
            Assert.Single(remaining);
            Assert.Equal("Low", remaining.First().DisplayArtist);
        }

        [Fact]
        public void ProcessStagedRecommendations_OrdersByConfidenceDescending()
        {
            // Arrange
            var recommendations = new[]
            {
                new ResolvedRecommendation { Status = ResolutionStatus.Resolved, Confidence = 0.5, DisplayArtist = "Mid" },
                new ResolvedRecommendation { Status = ResolutionStatus.Resolved, Confidence = 0.7, DisplayArtist = "High" },
                new ResolvedRecommendation { Status = ResolutionStatus.Resolved, Confidence = 0.6, DisplayArtist = "MidHigh" }
            };

            foreach (var rec in recommendations)
            {
                _staging.StageRecommendation(rec);
            }

            // Act
            var processed = _staging.ProcessStagedRecommendations(0.5);

            // Assert
            Assert.Equal(3, processed.Count);
            Assert.Equal("High", processed[0].DisplayArtist);
            Assert.Equal("MidHigh", processed[1].DisplayArtist);
            Assert.Equal("Mid", processed[2].DisplayArtist);
        }

        [Fact]
        public void ClearStagedRecommendations_RemovesAll()
        {
            // Arrange
            for (int i = 0; i < 5; i++)
            {
                _staging.StageRecommendation(new ResolvedRecommendation
                {
                    Status = ResolutionStatus.Resolved,
                    Confidence = 0.5,
                    DisplayArtist = $"Artist {i}"
                });
            }

            // Act
            _staging.ClearStagedRecommendations();
            var remaining = _staging.GetStagedRecommendations();

            // Assert
            Assert.Empty(remaining);
        }

        [Fact]
        public void GetStagingStats_ReturnsCorrectStats()
        {
            // Arrange
            var recommendations = new[]
            {
                new ResolvedRecommendation { Status = ResolutionStatus.Resolved, Confidence = 0.3 },  // Low
                new ResolvedRecommendation { Status = ResolutionStatus.Resolved, Confidence = 0.45 }, // Low
                new ResolvedRecommendation { Status = ResolutionStatus.Resolved, Confidence = 0.65 }, // Medium
                new ResolvedRecommendation { Status = ResolutionStatus.Resolved, Confidence = 0.7 },  // Medium
                new ResolvedRecommendation { Status = ResolutionStatus.Resolved, Confidence = 0.75 }  // Medium (just under high threshold)
            };

            foreach (var rec in recommendations)
            {
                _staging.StageRecommendation(rec);
            }

            // Act
            var stats = _staging.GetStagingStats();

            // Assert
            Assert.Equal(5, stats.TotalStaged);
            Assert.Equal(0, stats.HighConfidence); // None >= 0.8
            Assert.Equal(3, stats.MediumConfidence); // 0.65, 0.7, 0.75
            Assert.Equal(2, stats.LowConfidence); // 0.3, 0.45
            Assert.Equal(0.57, stats.AverageConfidence, 2); // (0.3 + 0.45 + 0.65 + 0.7 + 0.75) / 5
        }

        [Fact]
        public void GetStagingStats_EmptyStaging_ReturnsZeroStats()
        {
            // Act
            var stats = _staging.GetStagingStats();

            // Assert
            Assert.Equal(0, stats.TotalStaged);
            Assert.Equal(0, stats.HighConfidence);
            Assert.Equal(0, stats.MediumConfidence);
            Assert.Equal(0, stats.LowConfidence);
            Assert.Equal(0, stats.AverageConfidence);
        }

        [Fact]
        public void StageRecommendation_Null_DoesNotThrow()
        {
            // Act & Assert - Should not throw
            _staging.StageRecommendation(null);
            var staged = _staging.GetStagedRecommendations();
            Assert.Empty(staged);
        }

        [Fact]
        public void ConcurrentOperations_ThreadSafe()
        {
            // Arrange
            var tasks = new List<Task>();

            // Act - Perform concurrent operations
            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks.Add(Task.Run(() =>
                {
                    var rec = new ResolvedRecommendation
                    {
                        Status = ResolutionStatus.Resolved,
                        Confidence = 0.5 + (index * 0.001),
                        DisplayArtist = $"Artist {index}"
                    };
                    _staging.StageRecommendation(rec);
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Assert
            var staged = _staging.GetStagedRecommendations();
            Assert.Equal(100, staged.Count);
        }
    }

    public class RecommendationReviewTests
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly Mock<IRecommendationStaging> _stagingMock;
        private readonly Mock<IMusicBrainzResolver> _resolverMock;
        private readonly RecommendationReview _review;

        public RecommendationReviewTests()
        {
            _loggerMock = new Mock<Logger>();
            _stagingMock = new Mock<IRecommendationStaging>();
            _resolverMock = new Mock<IMusicBrainzResolver>();

            _review = new RecommendationReview(
                _loggerMock.Object,
                _stagingMock.Object,
                _resolverMock.Object);
        }

        [Fact]
        public async Task ReviewAndImproveRecommendations_HighConfidence_AutoApproved()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "High Confidence Artist",
                Album = "High Confidence Album"
            };

            var resolved = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.9,
                DisplayArtist = "High Confidence Artist",
                DisplayAlbum = "High Confidence Album",
                OriginalRecommendation = recommendation
            };

            _resolverMock.Setup(r => r.ResolveRecommendation(recommendation))
                .ReturnsAsync(resolved);

            _stagingMock.Setup(s => s.GetStagingStats())
                .Returns(new StagingStats());

            // Act
            var result = await _review.ReviewAndImproveRecommendations(
                new List<Recommendation> { recommendation }, 0.7);

            // Assert
            Assert.Single(result);
            Assert.Equal(resolved, result.First());
            _stagingMock.Verify(s => s.StageRecommendation(It.IsAny<ResolvedRecommendation>()), Times.Never);
        }

        [Fact]
        public async Task ReviewAndImproveRecommendations_LowConfidence_Staged()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Low Confidence Artist",
                Album = "Low Confidence Album"
            };

            var resolved = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.5,
                DisplayArtist = "Low Confidence Artist",
                DisplayAlbum = "Low Confidence Album",
                OriginalRecommendation = recommendation
            };

            _resolverMock.Setup(r => r.ResolveRecommendation(recommendation))
                .ReturnsAsync(resolved);

            // Setup improved match
            var improvedRec = new Recommendation
            {
                Artist = "Low Confidence Artist",
                Album = "Low Confidence Album"
            };

            var improvedResolved = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.8,
                DisplayArtist = "Low Confidence Artist",
                OriginalRecommendation = improvedRec
            };

            _resolverMock.Setup(r => r.ResolveRecommendation(It.Is<Recommendation>(
                    rec => rec.Artist != recommendation.Artist)))
                .ReturnsAsync(improvedResolved);

            _stagingMock.Setup(s => s.GetStagingStats())
                .Returns(new StagingStats { TotalStaged = 1 });

            // Act
            var result = await _review.ReviewAndImproveRecommendations(
                new List<Recommendation> { recommendation }, 0.7);

            // Assert
            _stagingMock.Verify(s => s.StageRecommendation(resolved), Times.Once);
        }

        [Fact]
        public async Task ReviewAndImproveRecommendations_NotResolved_Skipped()
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = "Not Found Artist",
                Album = "Not Found Album"
            };

            var resolved = new ResolvedRecommendation
            {
                Status = ResolutionStatus.NotFound,
                Reason = "Artist not found"
            };

            _resolverMock.Setup(r => r.ResolveRecommendation(recommendation))
                .ReturnsAsync(resolved);

            _stagingMock.Setup(s => s.GetStagingStats())
                .Returns(new StagingStats());

            // Act
            var result = await _review.ReviewAndImproveRecommendations(
                new List<Recommendation> { recommendation }, 0.7);

            // Assert
            Assert.Empty(result);
            _stagingMock.Verify(s => s.StageRecommendation(It.IsAny<ResolvedRecommendation>()), Times.Never);
        }

        [Theory]
        [InlineData("The Beatles", "Beatles", true)]
        [InlineData("Beatles", "The Beatles", true)]
        [InlineData("R.E.M.", "REM", true)]
        public async Task TryImproveRecommendations_GeneratesAlternativeSearches(
            string original, string alternative, bool shouldImprove)
        {
            // Arrange
            var recommendation = new Recommendation
            {
                Artist = original,
                Album = "Test Album"
            };

            var lowConfidenceResolved = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.5,
                OriginalRecommendation = recommendation
            };

            var improvedResolved = new ResolvedRecommendation
            {
                Status = ResolutionStatus.Resolved,
                Confidence = 0.9,
                DisplayArtist = alternative,
                OriginalRecommendation = new Recommendation { Artist = alternative }
            };

            _resolverMock.Setup(r => r.ResolveRecommendation(It.Is<Recommendation>(
                    rec => rec.Artist == original)))
                .ReturnsAsync(lowConfidenceResolved);

            if (shouldImprove)
            {
                _resolverMock.Setup(r => r.ResolveRecommendation(It.Is<Recommendation>(
                        rec => rec.Artist == alternative)))
                    .ReturnsAsync(improvedResolved);
            }

            _stagingMock.Setup(s => s.GetStagingStats())
                .Returns(new StagingStats());

            // Act
            var result = await _review.ReviewAndImproveRecommendations(
                new List<Recommendation> { recommendation }, 0.7);

            // Assert
            if (shouldImprove)
            {
                Assert.NotEmpty(result);
            }
        }
    }
}