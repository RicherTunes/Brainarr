using System;
using System.Collections.Generic;
using System.Globalization;
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
            lib.Setup(x => x.AnalyzeLibrary()).Returns(new LibraryProfile
            {
                TotalArtists = 30,
                TotalAlbums = 120,
                TopGenres = new Dictionary<string, int> { ["Rock"] = 40, ["Jazz"] = 8, ["Ambient"] = 4 },
                Metadata = new Dictionary<string, object>
                {
                    ["GenreDistribution"] = new Dictionary<string, double>
                    {
                        ["Rock"] = 68.0,
                        ["Jazz"] = 6.0,
                        ["Ambient"] = 2.0
                    },
                    ["PreferredEras"] = new List<string> { "Modern", "Contemporary" },
                    ["NewReleaseRatio"] = 0.52
                }
            });

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
            var triageAdvisorType = typeof(BrainarrOrchestrator).Assembly.GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Core.RecommendationTriageAdvisor");
            var triageAdvisor = Activator.CreateInstance(triageAdvisorType!);
            var handler = Activator.CreateInstance(handlerType!, queue, history, styleCatalog, triageAdvisor, (Action)null, logger);
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
        public void Review_GetTriageOptions_ReturnsSuggestedActions()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                queue.Enqueue(new[]
                {
                    new Recommendation
                    {
                        Artist = "DupArtist",
                        Album = "DupAlbum",
                        Confidence = 0.15,
                        Reason = "possible duplicate already in library"
                    },
                    new Recommendation
                    {
                        Artist = "StrongArtist",
                        Album = "StrongAlbum",
                        Confidence = 0.92,
                        ArtistMusicBrainzId = "mbid-a",
                        AlbumMusicBrainzId = "mbid-b"
                    }
                });

                var settings = new BrainarrSettings
                {
                    MinConfidence = 0.5,
                    RequireMbids = true,
                    RecommendationMode = RecommendationMode.SpecificAlbums
                };

                var res = orch.HandleAction("review/gettriageoptions", new Dictionary<string, string>(), settings);
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("\"summary\"", json);
                Assert.Contains("\"reject\"", json);
                Assert.Contains("\"accept\"", json);
                Assert.Contains("\"reasonCodes\"", json);
                Assert.Contains("\"explanation\"", json);
                Assert.Contains("DUPLICATE_SIGNAL", json);
                Assert.Contains("REJECT", json);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void Planning_GetGapPlan_ReturnsTargets()
        {
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                var res = orch.HandleAction("planning/getgapplan", new Dictionary<string, string>(), new BrainarrSettings());
                var json = JsonSerializer.Serialize(res);
                Assert.Contains("\"options\"", json);
                Assert.Contains("Catalog Backfill", json);
                Assert.Contains("Ambient", json);
                Assert.Contains("Lift", json);
                Assert.Contains("\"whyNow\"", json);
                Assert.Contains("\"evidence\"", json);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void Planning_GetGapPlan_GoldenSnapshot_IsStable()
        {
            var savedCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var orch = Create(out var md, out var queue, out var tmp);
            try
            {
                var result = orch.HandleAction("planning/getgapplan", new Dictionary<string, string>(), new BrainarrSettings());
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(result);
                using var doc = JsonDocument.Parse(json);
                var entries = doc.RootElement
                    .GetProperty("options")
                    .EnumerateArray()
                    .Select(option => new
                    {
                        value = option.GetProperty("value").GetString(),
                        category = option.GetProperty("category").GetString(),
                        target = option.GetProperty("target").GetString(),
                        priority = option.GetProperty("priority").GetInt32(),
                        confidence = Math.Round(option.GetProperty("confidence").GetDouble(), 2),
                        expectedLift = Math.Round(option.GetProperty("expectedLift").GetDouble(), 2),
                        evidence = option.GetProperty("evidence").EnumerateArray().Select(item => item.GetString()).ToArray()
                    })
                    .ToArray();

                var snapshot = new
                {
                    count = entries.Length,
                    entries
                };

                var actualJson = Normalize(JsonSerializer.Serialize(snapshot, options));
                Assert.Equal(ExpectedGapPlanSnapshotJson, actualJson);
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
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

        private static string Normalize(string value)
        {
            return value.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private const string ExpectedGapPlanSnapshotJson =
            "{\n" +
            "  \"count\": 4,\n" +
            "  \"entries\": [\n" +
            "    {\n" +
            "      \"value\": \"style:Ambient\",\n" +
            "      \"category\": \"style\",\n" +
            "      \"target\": \"Ambient\",\n" +
            "      \"priority\": 90,\n" +
            "      \"confidence\": 0.88,\n" +
            "      \"expectedLift\": 0.06,\n" +
            "      \"evidence\": [\n" +
            "        \"genre_share=2.0%\",\n" +
            "        \"target_floor=8.0%\",\n" +
            "        \"gap=6.0pp\"\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"value\": \"era:Classic\",\n" +
            "      \"category\": \"era\",\n" +
            "      \"target\": \"Classic\",\n" +
            "      \"priority\": 80,\n" +
            "      \"confidence\": 0.79,\n" +
            "      \"expectedLift\": 0.2,\n" +
            "      \"evidence\": [\n" +
            "        \"preferred_eras=[Modern, Contemporary]\",\n" +
            "        \"missing_era=Classic\"\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"value\": \"era-balance:Catalog Backfill\",\n" +
            "      \"category\": \"era-balance\",\n" +
            "      \"target\": \"Catalog Backfill\",\n" +
            "      \"priority\": 75,\n" +
            "      \"confidence\": 0.74,\n" +
            "      \"expectedLift\": 0.17,\n" +
            "      \"evidence\": [\n" +
            "        \"new_release_ratio=52 %\",\n" +
            "        \"target_band=15%-35%\"\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"value\": \"style:Jazz\",\n" +
            "      \"category\": \"style\",\n" +
            "      \"target\": \"Jazz\",\n" +
            "      \"priority\": 70,\n" +
            "      \"confidence\": 0.72,\n" +
            "      \"expectedLift\": 0.02,\n" +
            "      \"evidence\": [\n" +
            "        \"genre_share=6.0%\",\n" +
            "        \"target_floor=8.0%\",\n" +
            "        \"gap=2.0pp\"\n" +
            "      ]\n" +
            "    }\n" +
            "  ]\n" +
            "}";
    }
}
