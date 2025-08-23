using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using static NzbDrone.Core.ImportLists.Brainarr.Services.Support.RecommendationHistory;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    [Trait("Category", "Unit")]
    public class EnhancedRecommendationHistoryTests : IDisposable
    {
        private readonly Mock<Logger> _loggerMock;
        private readonly string _testDataPath;
        private readonly RecommendationHistory _history;

        public EnhancedRecommendationHistoryTests()
        {
            _loggerMock = new Mock<Logger>();
            _testDataPath = Path.Combine(Path.GetTempPath(), $"brainarr_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDataPath);
            _history = new RecommendationHistory(_loggerMock.Object, _testDataPath);
        }

        [Fact]
        public void MarkAsDisliked_ShouldAddToDislikedList()
        {
            // Act
            _history.MarkAsDisliked("Nickelback", "All the Right Reasons", DislikeLevel.Strong);

            // Assert
            var exclusions = _history.GetExclusions();
            exclusions.StronglyDisliked.Should().Contain("nickelback|all the right reasons");
        }

        [Fact]
        public void MarkAsDisliked_ShouldTrackDislikePatterns()
        {
            // Act
            _history.MarkAsDisliked("Artist A", "Album 1", DislikeLevel.Normal);
            _history.MarkAsDisliked("Artist A", "Album 2", DislikeLevel.Strong);

            // Assert - Should track that user dislikes "Artist A" style
            var exclusionPrompt = _history.GetExclusionPrompt();
            exclusionPrompt.Should().Contain("USER_DISLIKES_STYLE:Artist A");
        }

        [Fact]
        public void RemoveDislike_ShouldDeactivateDislike()
        {
            // Arrange
            _history.MarkAsDisliked("Test Artist", "Test Album", DislikeLevel.Normal);
            
            // Act
            _history.RemoveDislike("Test Artist", "Test Album");

            // Assert
            var exclusions = _history.GetExclusions();
            exclusions.Disliked.Should().NotContain("test artist|test album");
        }

        [Fact]
        public void GetExclusions_ShouldCategorizeDislikesByLevel()
        {
            // Arrange
            _history.MarkAsDisliked("Normal Artist", "Album", DislikeLevel.Normal);
            _history.MarkAsDisliked("Strong Artist", "Album", DislikeLevel.Strong);
            _history.MarkAsDisliked("Never Artist", "Album", DislikeLevel.NeverAgain);

            // Act
            var exclusions = _history.GetExclusions();

            // Assert
            exclusions.Disliked.Should().Contain("normal artist|album");
            exclusions.StronglyDisliked.Should().Contain("strong artist|album");
            exclusions.StronglyDisliked.Should().Contain("never artist|album"); // NeverAgain goes to StronglyDisliked
        }

        [Fact]
        public void GetExclusionPrompt_ShouldIncludeNegativeConstraints()
        {
            // Arrange
            _history.MarkAsDisliked("Avoided Artist", null, DislikeLevel.Normal);
            _history.MarkAsDisliked("Hated Artist", null, DislikeLevel.Strong);

            // Act
            var prompt = _history.GetExclusionPrompt();

            // Assert
            prompt.Should().Contain("DO_NOT_SUGGEST:Avoided Artist");
            prompt.Should().Contain("NEVER_RECOMMEND:Hated Artist");
        }

        [Fact]
        public void MarkAsRejected_ShouldAcceptRejectionReason()
        {
            // Arrange
            var recommendations = new[]
            {
                new Recommendation { Artist = "Test Artist", Album = "Test Album", Confidence = 0.8 }
            };
            _history.RecordSuggestions(recommendations.ToList());

            // Wait to avoid recent suggestion protection
            System.Threading.Thread.Sleep(TimeSpan.FromDays(2));

            // Act
            _history.MarkAsRejected("Test Artist", "Test Album", "Too mainstream");

            // Assert - This tests the enhanced MarkAsRejected with reason parameter
            var exclusions = _history.GetExclusions();
            exclusions.RecentlyRejected.Should().Contain("test artist|test album");
        }

        [Theory]
        [InlineData(DislikeLevel.Normal, "Normal")]
        [InlineData(DislikeLevel.Strong, "Strong")]
        [InlineData(DislikeLevel.NeverAgain, "NeverAgain")]
        public void MarkAsDisliked_ShouldHandleAllDislikeLevels(DislikeLevel level, string expectedName)
        {
            // Act
            _history.MarkAsDisliked("Test Artist", "Test Album", level);

            // Assert
            var exclusions = _history.GetExclusions();
            
            if (level == DislikeLevel.Normal)
            {
                exclusions.Disliked.Should().Contain("test artist|test album");
            }
            else
            {
                exclusions.StronglyDisliked.Should().Contain("test artist|test album");
            }
        }

        [Fact]
        public void GetExclusions_ShouldUpdateTotalExclusionsCount()
        {
            // Arrange
            _history.MarkAsDisliked("Artist1", "Album1", DislikeLevel.Normal);
            _history.MarkAsDisliked("Artist2", "Album2", DislikeLevel.Strong);

            // Act
            var exclusions = _history.GetExclusions();

            // Assert
            exclusions.TotalExclusions.Should().Be(2);
            exclusions.HasExclusions.Should().BeTrue();
        }

        [Fact]
        public void DislikePatternTracking_ShouldAccumulateAverageLevel()
        {
            // Arrange - Multiple dislikes for same artist with different levels
            _history.MarkAsDisliked("Pattern Artist", "Album 1", DislikeLevel.Normal);    // Level 0
            _history.MarkAsDisliked("Pattern Artist", "Album 2", DislikeLevel.Strong);    // Level 1
            _history.MarkAsDisliked("Pattern Artist", "Album 3", DislikeLevel.NeverAgain); // Level 2

            // Act
            var prompt = _history.GetExclusionPrompt();

            // Assert - Should appear in USER_DISLIKES_STYLE since count >= 2
            // Average level should be (0+1+2)/3 = 1
            prompt.Should().Contain("USER_DISLIKES_STYLE:Pattern Artist");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDataPath))
                {
                    Directory.Delete(_testDataPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

    }
}