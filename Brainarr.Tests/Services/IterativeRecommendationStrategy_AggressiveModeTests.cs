using System.Collections.Generic;
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
    public class IterativeRecommendationStrategy_AggressiveModeTests
    {
        [Fact]
        public async Task Aggressive_IgnoresSuccessRate_GoesUntilHysteresis()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var mockProvider = new Mock<IAIProvider>();
            var mockPrompt = new Mock<ILibraryAwarePromptBuilder>();
            mockPrompt.Setup(p => p.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(), It.IsAny<List<Artist>>(), It.IsAny<List<Album>>(), It.IsAny<BrainarrSettings>(), It.IsAny<bool>()))
                .Returns("prompt");

            var strategy = new IterativeRecommendationStrategy(logger, mockPrompt.Object);

            var settings = new BrainarrSettings
            {
                MaxRecommendations = 5,
                Provider = AIProvider.Ollama,
                BackfillStrategy = BackfillStrategy.Aggressive
            };

            var profile = new LibraryProfile { TopArtists = new List<string> { "Existing Artist" } };
            var existingArtists = new List<Artist> { new Artist { Name = "Existing Artist" } };
            var existingAlbums = new List<Album> { new Album { Title = "Existing Album", ArtistMetadata = new ArtistMetadata { Name = "Existing Artist" } } };

            var duplicateRecs = new List<Recommendation>
            {
                new Recommendation { Artist = "Existing Artist", Album = "Existing Album" }
            };
            mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>())).ReturnsAsync(duplicateRecs);

            var result = await strategy.GetIterativeRecommendationsAsync(
                mockProvider.Object, profile, existingArtists, existingAlbums, settings);

            Assert.Empty(result);
            // Expect 3 calls due to zero-success hysteresis threshold (Aggressive uses min 3)
            mockProvider.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.Exactly(3));
        }
    }
}
