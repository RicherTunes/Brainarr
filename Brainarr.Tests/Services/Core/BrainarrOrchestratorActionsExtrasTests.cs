using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Brainarr.Tests.Helpers;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrOrchestratorActionsExtrasTests
    {
        private BrainarrOrchestrator Create(
            out Mock<IModelDetectionService> modelDetection,
            out ReviewQueueService queue,
            out string tmp)
        {
            var providerFactory = new Mock<IProviderFactory>();
            var lib = new Mock<ILibraryAnalyzer>();
            var cache = new Mock<IRecommendationCache>();
            var health = new Mock<IProviderHealthMonitor>();
            var validator = new Mock<IRecommendationValidator>();
            modelDetection = new Mock<IModelDetectionService>();
            var http = new Mock<IHttpClient>();
            var logger = Helpers.TestLogger.CreateNullLogger();

            var orch = new BrainarrOrchestrator(
                logger,
                providerFactory.Object,
                lib.Object,
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                duplicationPrevention: null,
                breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object,
                duplicateFilter: Mock.Of<IDuplicateFilterService>());

            tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            queue = new ReviewQueueService(logger, tmp);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var qf = typeof(BrainarrOrchestrator).GetField("_reviewQueue", flags);
            qf!.SetValue(orch, queue);

            // Also swap the ReviewQueueActionHandler so it uses the temp-backed queue
            var hf = typeof(BrainarrOrchestrator).GetField("_history", flags);
            var history = hf!.GetValue(orch);
            var scf = typeof(BrainarrOrchestrator).GetField("_styleCatalog", flags);
            var styleCatalog = scf!.GetValue(orch);
            var handlerType = typeof(BrainarrOrchestrator).Assembly.GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Core.ReviewQueueActionHandler");
            var handler = Activator.CreateInstance(handlerType!, queue, history, styleCatalog, (Action)null, logger);
            var rhf = typeof(BrainarrOrchestrator).GetField("_reviewQueueHandler", flags);
            rhf!.SetValue(orch, handler);

            return orch;
        }

        [Fact]
        public void MetricsGet_ReturnsShape()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                queue.Enqueue(new[]
                {
                    new NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation { Artist = "A", Album = "B" },
                    new NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation { Artist = "C", Album = "D" }
                });
                // accept one
                queue.SetStatus("A", "B", ReviewQueueService.ReviewStatus.Accepted);
                queue.DequeueAccepted();

                var settings = new BrainarrSettings();
                var res = orch.HandleAction("metrics/get", new Dictionary<string, string>(), settings);
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("\"review\"", json);
                Assert.Contains("\"provider\"", json);
                Assert.Contains("\"artistPromotion\"", json);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void Review_GetOptions_ReturnsItems()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                queue.Enqueue(new[]
                {
                    new NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation { Artist = "X", Album = "B" },
                    new NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation { Artist = "A", Album = "A1" }
                });
                var settings = new BrainarrSettings();
                var res = orch.HandleAction("review/getoptions", new Dictionary<string, string>(), settings);
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("\"options\"", json);
                Assert.Contains("X", json);
                Assert.Contains("A", json);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void Review_GetSummaryOptions_ReturnsCounts()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                queue.Enqueue(new[]
                {
                    new NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation { Artist = "X", Album = "B" }
                });
                queue.SetStatus("X", "B", ReviewQueueService.ReviewStatus.Rejected);

                var settings = new BrainarrSettings();
                var res = orch.HandleAction("review/getsummaryoptions", new Dictionary<string, string>(), settings);
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("Rejected:", json);
                Assert.Contains("Pending:", json);
                Assert.Contains("Never Again:", json);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void UnknownAction_ReturnsErrorObject()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                var res = orch.HandleAction("nope/unknown", new Dictionary<string, string>(), new BrainarrSettings());
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("\"error\"", json);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void GetModelOptions_Static_ForCloudProvider()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
                var res = orch.HandleAction("getModelOptions", new Dictionary<string, string>(), settings);
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("\"options\"", json);
                Assert.Contains("gpt-4o", json, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void GetModelOptions_Ollama_WithOverrideBaseUrl_UsesDetection()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                md.Setup(m => m.GetOllamaModelsAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { "qwen2.5:latest", "llama3.2" });
                var settings = new BrainarrSettings { Provider = AIProvider.Ollama, OllamaUrl = "http://old" };
                var q = new Dictionary<string, string> { ["provider"] = "Ollama", ["baseUrl"] = "http://override" };
                var res = orch.HandleAction("getModelOptions", q, settings);
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("qwen2.5:latest", json);
                Assert.Contains("llama3.2", json);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void DetectModels_LMStudio_ReturnsDetectedList()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                md.Setup(m => m.GetLMStudioModelsAsync(It.IsAny<string>())).ReturnsAsync(new List<string> { "local-model" });
                var settings = new BrainarrSettings { Provider = AIProvider.LMStudio, LMStudioUrl = "http://lm" };
                var res = orch.HandleAction("detectmodels", new Dictionary<string, string>(), settings);
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("local-model", json);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }
    }
}
