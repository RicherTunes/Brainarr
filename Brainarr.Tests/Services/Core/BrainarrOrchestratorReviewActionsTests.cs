using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
    public class BrainarrOrchestratorReviewActionsTests : IDisposable
    {
        private readonly Mock<IProviderFactory> _providerFactory = new();
        private readonly Mock<ILibraryAnalyzer> _libraryAnalyzer = new();
        private readonly Mock<IRecommendationCache> _cache = new();
        private readonly Mock<IProviderHealthMonitor> _health = new();
        private readonly Mock<IRecommendationValidator> _validator = new();
        private readonly Mock<IModelDetectionService> _models = new();
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();
        private readonly string _tempRoot;
        private readonly ReviewQueueService _queue;
        private readonly RecommendationHistory _history;
        private readonly BrainarrOrchestrator _orch;

        public BrainarrOrchestratorReviewActionsTests()
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
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);

            _queue = new ReviewQueueService(_logger, _tempRoot);
            _history = new RecommendationHistory(_logger, _tempRoot);

            // Swap orchestrator private fields to use temp-backed services
            var qf = typeof(BrainarrOrchestrator).GetField("_reviewQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            qf!.SetValue(_orch, _queue);
            var hf = typeof(BrainarrOrchestrator).GetField("_history", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            hf!.SetValue(_orch, _history);
        }

        [Fact]
        public void Review_GetQueue_ReturnsPending()
        {
            _queue.Enqueue(new[]
            {
                new Recommendation { Artist = "A", Album = "B", Confidence = 0.9 },
                new Recommendation { Artist = "C", Album = "D", Confidence = 0.8 }
            });

            var settings = new BrainarrSettings();
            var result = _orch.HandleAction("review/getqueue", new Dictionary<string, string>(), settings);

            var json = JsonSerializer.Serialize(result);
            json.Should().Contain("\"items\"");
            _queue.GetPending().Should().HaveCount(2);
        }

        [Fact]
        public void Review_Accept_Then_Apply_Releases()
        {
            _queue.Enqueue(new[] { new Recommendation { Artist = "Acc", Album = "Alb" } });

            var settings = new BrainarrSettings();
            var q = new Dictionary<string, string> { ["artist"] = "Acc", ["album"] = "Alb" };
            var ok = _orch.HandleAction("review/accept", q, settings);
            JsonSerializer.Serialize(ok).Should().Contain("\"ok\":true");

            var apply = _orch.HandleAction("review/apply", new Dictionary<string, string>(), settings);
            var applyJson = JsonSerializer.Serialize(apply);
            applyJson.Should().Contain("\"ok\":true");
            applyJson.Should().Contain("\"released\":1");
            _queue.GetPending().Should().BeEmpty();
        }

        [Fact]
        public async Task Review_Reject_RecordsHistory_WhenSuggested()
        {
            // Ensure suggestion exists and wait minimal time to allow rejection record (test mode uses ~5ms)
            _history.RecordSuggestions(new List<Recommendation> { new Recommendation { Artist = "R1", Album = "A1", Confidence = 0.9 } });
            await Task.Delay(10);
            _queue.Enqueue(new[] { new Recommendation { Artist = "R1", Album = "A1" } });

            var settings = new BrainarrSettings();
            var q = new Dictionary<string, string> { ["artist"] = "R1", ["album"] = "A1", ["notes"] = "no" };
            var res = _orch.HandleAction("review/reject", q, settings);
            JsonSerializer.Serialize(res).Should().Contain("\"ok\":true");

            // Verify history JSON updated
            var histFile = Path.Combine(_tempRoot, "plugins", "RicherTunes", "Brainarr", "data", "recommendation_history.json");
            File.Exists(histFile).Should().BeTrue();
            var text = File.ReadAllText(histFile);
            text.Should().Contain("\"Rejected\"");
            text.Should().Contain("R1");
        }

        [Fact]
        public void Review_Never_SetsDisliked()
        {
            _queue.Enqueue(new[] { new Recommendation { Artist = "N1", Album = "A2" } });

            var settings = new BrainarrSettings();
            var q = new Dictionary<string, string> { ["artist"] = "N1", ["album"] = "A2" };
            var res = _orch.HandleAction("review/never", q, settings);
            JsonSerializer.Serialize(res).Should().Contain("\"ok\":true");

            var histFile = Path.Combine(_tempRoot, "plugins", "RicherTunes", "Brainarr", "data", "recommendation_history.json");
            var text = File.ReadAllText(histFile);
            text.Should().Contain("\"Disliked\"");
            text.Should().Contain("N1");
        }

        [Fact]
        public void Review_Clear_ClearsSelections()
        {
            var settings = new BrainarrSettings { ReviewApproveKeys = new[] { "A|B", "C|D" } };
            var res = _orch.HandleAction("review/clear", new Dictionary<string, string>(), settings);
            JsonSerializer.Serialize(res).Should().Contain("\"cleared\":true");
            settings.ReviewApproveKeys.Should().BeEmpty();
        }

        [Fact]
        public async Task Review_RejectSelected_UpdatesStatuses_AndClears()
        {
            _queue.Enqueue(new[]
            {
                new Recommendation { Artist = "S1", Album = "L1" },
                new Recommendation { Artist = "S2", Album = "L2" }
            });
            _history.RecordSuggestions(new List<Recommendation>
            {
                new Recommendation { Artist = "S1", Album = "L1" },
                new Recommendation { Artist = "S2", Album = "L2" }
            });
            await Task.Delay(10);

            var settings = new BrainarrSettings { ReviewApproveKeys = new[] { "S1|L1", "S2|L2" } };
            var res = _orch.HandleAction("review/rejectselected", new Dictionary<string, string>(), settings);
            var json = JsonSerializer.Serialize(res);
            json.Should().Contain("\"ok\":true");
            settings.ReviewApproveKeys.Should().BeEmpty();

            var counts = _queue.GetCounts();
            counts.rejected.Should().Be(2);
        }

        [Fact]
        public void Review_NeverSelected_UpdatesStatuses_AndClears()
        {
            _queue.Enqueue(new[]
            {
                new Recommendation { Artist = "T1", Album = "M1" },
                new Recommendation { Artist = "T2", Album = "M2" }
            });

            var settings = new BrainarrSettings { ReviewApproveKeys = new[] { "T1|M1", "T2|M2" } };
            var res = _orch.HandleAction("review/neverselected", new Dictionary<string, string>(), settings);
            var json = JsonSerializer.Serialize(res);
            json.Should().Contain("\"ok\":true");
            settings.ReviewApproveKeys.Should().BeEmpty();

            var counts = _queue.GetCounts();
            counts.never.Should().Be(2);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }
    }
}
