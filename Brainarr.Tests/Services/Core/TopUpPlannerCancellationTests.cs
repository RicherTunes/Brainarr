using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Integration-seam guard: TopUpPlanner is the SOLE caller of the iterative strategy, and its
    /// broad per-iteration catch previously re-swallowed the OperationCanceledException the strategy
    /// now re-throws on run-cancellation — silently returning a partial list as success and hiding
    /// the cancel from the orchestrator's cancellation handler. This pins that a cancelled run token
    /// propagates through TopUpPlanner instead of being re-swallowed (and that a provider's own
    /// timeout, run token NOT cancelled, is still treated as a recoverable partial).
    /// </summary>
    public class TopUpPlannerCancellationTests
    {
        private static (TopUpPlanner planner, Mock<IAIProvider> provider, Mock<ILibraryAnalyzer> analyzer,
            Mock<ILibraryAwarePromptBuilder> prompt, Mock<IDuplicationPrevention> dedup) Build()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var filter = new Mock<IDuplicateFilterService>();
            filter.Setup(f => f.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
                .Returns((List<ImportListItemInfo> items) => items ?? new List<ImportListItemInfo>());
            var planner = new TopUpPlanner(logger, filter.Object);

            var analyzer = new Mock<ILibraryAnalyzer>();
            analyzer.Setup(a => a.GetAllArtists()).Returns(new List<Artist>());
            analyzer.Setup(a => a.GetAllAlbums()).Returns(new List<Album>());

            var prompt = new Mock<ILibraryAwarePromptBuilder>();
            prompt.Setup(p => p.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(), It.IsAny<List<Artist>>(), It.IsAny<List<Album>>(),
                It.IsAny<BrainarrSettings>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns("prompt");

            var provider = new Mock<IAIProvider>();
            return (planner, provider, analyzer, prompt, new Mock<IDuplicationPrevention>());
        }

        [Fact]
        public async Task TopUpAsync_RunCancelled_PropagatesOperationCanceled()
        {
            var (planner, provider, analyzer, prompt, dedup) = Build();
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ThrowsAsync(new OperationCanceledException());

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<Task> act = async () => await planner.TopUpAsync(
                new BrainarrSettings { Provider = AIProvider.Ollama, MaxRecommendations = 10 },
                provider.Object, analyzer.Object, prompt.Object, dedup.Object,
                new LibraryProfile(), needed: 5, initialValidation: null, cancellationToken: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task TopUpAsync_ProviderTimeoutLiveToken_ReturnsPartialNotThrow()
        {
            // Provider's own timeout surfaces as OCE while the run token is NOT cancelled — must be
            // treated as a recoverable per-iteration failure (return collected items), not propagated.
            var (planner, provider, analyzer, prompt, dedup) = Build();
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                .ThrowsAsync(new OperationCanceledException());

            var result = await planner.TopUpAsync(
                new BrainarrSettings { Provider = AIProvider.Ollama, MaxRecommendations = 10 },
                provider.Object, analyzer.Object, prompt.Object, dedup.Object,
                new LibraryProfile(), needed: 5, initialValidation: null, cancellationToken: CancellationToken.None);

            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }
    }
}
