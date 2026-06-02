using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
    /// <summary>
    /// Discovery-mode escalation: when top-up iterations saturate (the library/history dedup keeps
    /// rejecting the same cluster), widen the effective discovery mode one step toward Exploratory
    /// rather than stopping under target. Pure decision helper is unit-tested here; the loop wiring
    /// is exercised live on the lidarr-e2e instance.
    /// </summary>
    public class DiscoveryEscalationTests
    {
        [Theory]
        [InlineData(DiscoveryMode.Similar, true, DiscoveryMode.Adjacent)]
        [InlineData(DiscoveryMode.Adjacent, true, DiscoveryMode.Exploratory)]
        public void TryEscalate_WidensOneStep(DiscoveryMode current, bool expectedCanEscalate, DiscoveryMode expectedNext)
        {
            var ok = IterativeRecommendationStrategy.TryEscalateDiscoveryMode(current, out var next);
            ok.Should().Be(expectedCanEscalate);
            next.Should().Be(expectedNext);
        }

        [Fact]
        public void TryEscalate_AtExploratory_CannotEscalate()
        {
            // Exploratory is the widest mode — escalation must terminate here (no infinite loop).
            var ok = IterativeRecommendationStrategy.TryEscalateDiscoveryMode(DiscoveryMode.Exploratory, out var next);
            ok.Should().BeFalse();
            next.Should().Be(DiscoveryMode.Exploratory, "unchanged when it can't widen further");
        }

        [Fact]
        public void TryEscalate_IsMonotonic_TerminatesInTwoStepsFromSimilar()
        {
            // Similar -> Adjacent -> Exploratory -> (stop). Bounds the escalation count.
            var mode = DiscoveryMode.Similar;
            var steps = 0;
            while (IterativeRecommendationStrategy.TryEscalateDiscoveryMode(mode, out var next))
            {
                next.Should().NotBe(mode, "each escalation must strictly widen");
                mode = next;
                steps++;
                steps.Should().BeLessThanOrEqualTo(2, "escalation must terminate, not loop");
            }
            mode.Should().Be(DiscoveryMode.Exploratory);
            steps.Should().Be(2);
        }

        // ---- loop integration: escalation extends the run only when the caller is aggressive --------

        private static async Task<int> CountProviderCallsAsync(bool callerAggressive)
        {
            var logger = LogManager.GetCurrentClassLogger();
            var prompt = new Mock<ILibraryAwarePromptBuilder>();
            prompt.Setup(p => p.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(), It.IsAny<List<Artist>>(), It.IsAny<List<Album>>(),
                It.IsAny<BrainarrSettings>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns("prompt");
            var strategy = new IterativeRecommendationStrategy(logger, prompt.Object);

            var provider = new Mock<IAIProvider>();
            var calls = 0;
            var dupes = new List<Recommendation> { new Recommendation { Artist = "Existing", Album = "Album" } };
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ReturnsAsync(() => { calls++; return dupes; });

            var profile = new LibraryProfile { TopArtists = new List<string> { "Existing" } };
            var artists = new List<Artist> { new Artist { Name = "Existing" } };
            var albums = new List<Album> { new Album { Title = "Album", ArtistMetadata = new ArtistMetadata { Name = "Existing" } } };
            var settings = new BrainarrSettings
            {
                MaxRecommendations = 5,
                Provider = AIProvider.Ollama,
                BackfillStrategy = BackfillStrategy.Aggressive, // gives a generous iteration budget either way
                DiscoveryMode = DiscoveryMode.Similar           // room to escalate twice
            };

            await strategy.GetIterativeRecommendationsAsync(
                provider.Object, profile, artists, albums, settings,
                shouldRecommendArtists: false, aggressiveGuarantee: callerAggressive);
            return calls;
        }

        [Fact]
        public async Task Escalation_WhenCallerAggressive_ExtendsRunBeyondNonAggressive()
        {
            // Identical all-duplicate setup. The aggressive (top-up) caller should escalate discovery on
            // saturation and burn more of the iteration budget trying to break out; the non-aggressive
            // caller keeps the base early-stop. This proves the escalation is wired into the loop AND
            // that it stays gated to the aggressive/top-up path (so the base strategy is unchanged).
            var aggressiveCalls = await CountProviderCallsAsync(callerAggressive: true);
            var plainCalls = await CountProviderCallsAsync(callerAggressive: false);

            aggressiveCalls.Should().BeGreaterThan(plainCalls,
                "discovery escalation should use more of the budget to break out of a saturated cluster");
        }
    }
}
