using System;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationCacheKeyVersionTests
    {
        private sealed class FakePlannerVersionProvider : IPlannerVersionProvider
        {
            private readonly string _v;
            public FakePlannerVersionProvider(string v) { _v = v; }
            public string GetConfigVersion() => _v;
        }

        private static BrainarrSettings MakeSettings()
        {
            return new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                ModelSelection = "qwen2.5:latest",
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                StyleFilters = new[] { "shoegaze", "dreampop" },
                RelaxStyleMatching = true,
                MaxSelectedStyles = 2
            };
        }

        private static LibraryProfile MakeProfile()
        {
            return new LibraryProfile
            {
                TotalArtists = 50,
                TotalAlbums = 200,
                TopGenres = new System.Collections.Generic.Dictionary<string, int>
                {
                    {"rock", 10}, {"jazz", 5}
                },
                TopArtists = new System.Collections.Generic.List<string> { "A", "B", "C" },
                RecentlyAdded = new System.Collections.Generic.List<string> { "X", "Y" }
            };
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CacheKey_Changes_When_PlannerConfigVersion_Changes()
        {
            var s = MakeSettings();
            var p = MakeProfile();

            var kbA = new RecommendationCacheKeyBuilder(new FakePlannerVersionProvider("2025-09-30-a"));
            var kbB = new RecommendationCacheKeyBuilder(new FakePlannerVersionProvider("2025-10-01-b"));

            var k1 = kbA.Build(s, p);
            var k2 = kbB.Build(s, p);

            Assert.NotEqual(k1, k2);
        }
    }
}
