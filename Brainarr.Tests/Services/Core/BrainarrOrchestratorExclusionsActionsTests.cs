using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Feature B8: "Never again" exclusions were write-only in the UI -- RecommendationHistory.RemoveDislike
    /// existed with zero production callers, so the only way to undo a "Never again" was hand-editing
    /// recommendation_history.json inside the running container. exclusions/get + exclusions/remove close
    /// that gap, mirroring the review/* action dispatch pattern (BrainarrOrchestrator.HandleAction).
    ///
    /// NOTE (verified while building this feature): RecommendationHistory.GetExclusions()/GetExclusionPrompt()
    /// have no production caller that feeds them into prompt-building or post-generation filtering today --
    /// "Never again" writes an inert record; nothing currently blocks a disliked artist from being
    /// re-suggested. These tests prove undo against the actual query surface that exists
    /// (GetExclusions()'s IsActive-gated categorization, mutated by MarkAsDisliked/RemoveDislike), which is
    /// the full extent of "the gate" this codebase defines. Wiring that gate into live recommendation
    /// generation is a separate, out-of-scope, pre-existing gap -- not introduced or masked by this feature.
    /// </summary>
    public class BrainarrOrchestratorExclusionsActionsTests : IDisposable
    {
        private readonly Mock<IProviderFactory> _providerFactory = new();
        private readonly Mock<ILibraryAnalyzer> _libraryAnalyzer = new();
        private readonly Mock<IRecommendationCache> _cache = new();
        private readonly Mock<IProviderHealthMonitor> _health = new();
        private readonly Mock<IRecommendationValidator> _validator = new();
        private readonly Mock<IModelDetectionService> _models = new();
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = TestLogger.CreateNullLogger();
        private readonly string _tempRoot;
        private readonly ReviewQueueService _queue;
        private readonly RecommendationHistory _history;
        private readonly BrainarrOrchestrator _orch;

        public BrainarrOrchestratorExclusionsActionsTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            _providerFactory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                .Returns(Mock.Of<IAIProvider>(p => p.ProviderName == "Test"));

            _orch = new BrainarrOrchestrator(
                _logger,
                _providerFactory.Object,
                _libraryAnalyzer.Object,
                _cache.Object,
                _health.Object,
                _validator.Object,
                _models.Object,
                _http.Object,
                duplicationPrevention: null,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object,
                duplicateFilter: Mock.Of<IDuplicateFilterService>());

            _queue = new ReviewQueueService(_logger, _tempRoot);
            _history = new RecommendationHistory(_logger, _tempRoot);

            // Reflection-swap the orchestrator's private fields onto temp-backed services --
            // matches the established convention in BrainarrOrchestratorReviewActionsTests.
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var qf = typeof(BrainarrOrchestrator).GetField("_reviewQueue", flags);
            qf!.SetValue(_orch, _queue);
            var hf = typeof(BrainarrOrchestrator).GetField("_history", flags);
            hf!.SetValue(_orch, _history);

            var scf = typeof(BrainarrOrchestrator).GetField("_styleCatalog", flags);
            var styleCatalog = scf!.GetValue(_orch);
            var handlerType = typeof(BrainarrOrchestrator).Assembly.GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Core.ReviewQueueActionHandler");
            var triageAdvisorType = typeof(BrainarrOrchestrator).Assembly.GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Core.RecommendationTriageAdvisor");
            var auditServiceType = typeof(BrainarrOrchestrator).Assembly.GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Support.ReviewActionAuditService");
            var triageAdvisor = Activator.CreateInstance(triageAdvisorType!);
            var auditService = Activator.CreateInstance(auditServiceType!, _logger, _tempRoot);
            var handler = Activator.CreateInstance(handlerType!, _queue, _history, styleCatalog, triageAdvisor, (Action)null, _logger, auditService);
            var rhf = typeof(BrainarrOrchestrator).GetField("_reviewQueueHandler", flags);
            rhf!.SetValue(_orch, handler);
        }

        [Fact]
        public void Exclusions_RoundTrip_MarkNeverAgain_Remove_ReEnables()
        {
            _queue.Enqueue(new[] { new Recommendation { Artist = "Nickelback", Album = "Dark Horse" } });

            var settings = new BrainarrSettings();
            var never = new Dictionary<string, string> { ["artist"] = "Nickelback", ["album"] = "Dark Horse" };

            // 1. Trigger the real "Never again" path (review/never), exactly like the UI does --
            // this is what calls RecommendationHistory.MarkAsDisliked(..., NeverAgain) in production.
            var neverResult = _orch.HandleAction("review/never", never, settings);
            JsonSerializer.Serialize(neverResult).Should().Contain("\"ok\":true");

            // Sanity: the dislike really landed in the exclusion store, categorized as strongly
            // disliked (NeverAgain folds into StronglyDisliked -- see RecommendationHistory.GetExclusions()).
            var before = _history.GetExclusions();
            before.StronglyDisliked.Should().Contain("nickelback|dark horse");

            // 2. Undo via the new action -- the only non-manual-JSON-edit path today.
            var removeResult = _orch.HandleAction("exclusions/remove", never, settings);
            var removeJson = JsonSerializer.Serialize(removeResult);
            removeJson.Should().Contain("\"ok\":true");
            removeJson.Should().Contain("\"found\":true");

            // 3. exclusions/get (the list-facing view) no longer shows the artist.
            var getResult = _orch.HandleAction("exclusions/get", new Dictionary<string, string>(), settings);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(getResult));
            var stronglyDisliked = doc.RootElement.GetProperty("StronglyDisliked").EnumerateArray().Select(e => e.GetString()).ToList();
            stronglyDisliked.Should().NotContain("nickelback|dark horse");

            // 4. THE KEY PROOF -- real re-enablement, not just list cosmetics: invoke the actual gate
            // predicate (GetExclusions()'s IsActive-driven categorization -- the only exclusion filter
            // this codebase defines) directly on the SAME RecommendationHistory instance the action
            // dispatched through, and confirm the artist is no longer reported as excluded under ANY
            // category. Any future caller that wires GetExclusions()/GetExclusionPrompt() into
            // recommendation filtering (none does today -- see class doc) would now treat this artist
            // as eligible again, because the underlying IsActive flag -- not just a derived list -- flipped.
            var after = _history.GetExclusions();
            after.StronglyDisliked.Should().NotContain("nickelback|dark horse");
            after.Disliked.Should().NotContain("nickelback|dark horse");
        }

        [Fact]
        public void Exclusions_Remove_NonExistentEntry_IsIdempotent_NoThrow()
        {
            var settings = new BrainarrSettings();
            var query = new Dictionary<string, string> { ["artist"] = "Never Marked Artist", ["album"] = "Some Album" };

            var result = _orch.HandleAction("exclusions/remove", query, settings);
            var json = JsonSerializer.Serialize(result);

            json.Should().Contain("\"ok\":true");
            json.Should().Contain("\"found\":false");
        }

        [Fact]
        public void Exclusions_Remove_MissingArtist_ReturnsValidationError()
        {
            var settings = new BrainarrSettings();
            var result = _orch.HandleAction("exclusions/remove", new Dictionary<string, string>(), settings);
            var json = JsonSerializer.Serialize(result);

            json.Should().Contain("\"ok\":false");
        }

        [Fact]
        public void Exclusions_Get_ReturnsEmpty_WhenNoneMarked()
        {
            var settings = new BrainarrSettings();
            var result = _orch.HandleAction("exclusions/get", new Dictionary<string, string>(), settings);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));

            doc.RootElement.GetProperty("HasExclusions").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("StronglyDisliked").GetArrayLength().Should().Be(0);
        }

        [Fact]
        public void Exclusions_Get_DoesNotLeakFieldsBeyondTheKnownExclusionShape()
        {
            _history.MarkAsDisliked("Leak Check Artist", "Leak Check Album", RecommendationHistory.DislikeLevel.NeverAgain);

            var settings = new BrainarrSettings();
            var result = _orch.HandleAction("exclusions/get", new Dictionary<string, string>(), settings);

            // Assert the returned CLR type IS the already-shaped RecommendationHistory.ExclusionList --
            // not an ad-hoc anonymous projection that could accidentally carry extra fields (file paths,
            // API keys, internal IDs) the way a hand-built response object might.
            result.Should().BeOfType<RecommendationHistory.ExclusionList>();

            var allowedProperties = new[] { "InLibrary", "RecentlyRejected", "OverSuggested", "Disliked", "StronglyDisliked", "HasExclusions", "TotalExclusions" };
            var actualProperties = result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);
            actualProperties.Should().BeEquivalentTo(allowedProperties);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }
    }
}
