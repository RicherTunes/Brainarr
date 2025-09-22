using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using NzbDrone.Core.Music;

namespace Brainarr.Tests.Services
{
    public class LibraryAwarePromptBuilderSimpleTests
    {
        [Fact]
        public void GetEffectiveTokenLimit_Responds_To_Strategy_And_Provider()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var b = new LibraryAwarePromptBuilder(logger);

            var balancedCloud = b.GetEffectiveTokenLimit(SamplingStrategy.Balanced, AIProvider.OpenAI);
            var balancedOllama = b.GetEffectiveTokenLimit(SamplingStrategy.Balanced, AIProvider.Ollama);
            var minimalCloud = b.GetEffectiveTokenLimit(SamplingStrategy.Minimal, AIProvider.OpenAI);
            var compCloud = b.GetEffectiveTokenLimit(SamplingStrategy.Comprehensive, AIProvider.OpenAI);

            Assert.True(balancedOllama > balancedCloud);
            Assert.True(compCloud > minimalCloud);
        }

        [Fact]
        public void BuildLibraryAwarePrompt_FallsBack_On_Exception()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var b = new LibraryAwarePromptBuilder(logger);
            var profile = new LibraryProfile { TopArtists = new List<string> { "A" }, TopGenres = new Dictionary<string, int> { ["rock"] = 1 } };

            // Force exception by passing null lists to sampling
            var prompt = b.BuildLibraryAwarePrompt(profile, null, null, new BrainarrSettings(), false);
            Assert.False(string.IsNullOrWhiteSpace(prompt));
        }

        [Fact]
        public void BuildLibraryAwarePrompt_Differs_By_SamplingStrategy()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var b = new LibraryAwarePromptBuilder(logger);
            var profile = new LibraryProfile
            {
                TopArtists = new List<string> { "A", "B", "C" },
                TopGenres = new Dictionary<string, int> { ["rock"] = 3, ["jazz"] = 2 }
            };
            var artists = new List<Artist>();
            var albums = new List<Album>();

            var s1 = new BrainarrSettings { SamplingStrategy = SamplingStrategy.Minimal };
            var s2 = new BrainarrSettings { SamplingStrategy = SamplingStrategy.Balanced };
            var s3 = new BrainarrSettings { SamplingStrategy = SamplingStrategy.Comprehensive };

            var p1 = b.BuildLibraryAwarePrompt(profile, artists, albums, s1);
            var p2 = b.BuildLibraryAwarePrompt(profile, artists, albums, s2);
            var p3 = b.BuildLibraryAwarePrompt(profile, artists, albums, s3);

            Assert.False(string.IsNullOrWhiteSpace(p1));
            Assert.False(string.IsNullOrWhiteSpace(p2));
            Assert.False(string.IsNullOrWhiteSpace(p3));
            Assert.NotEqual(p1, p2);
            Assert.NotEqual(p2, p3);
        }

        [Fact]
        public void EstimateTokens_Returns_Positive_Value()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var b = new LibraryAwarePromptBuilder(logger);
            var tokens = b.EstimateTokens("hello world this is a test string");
            Assert.True(tokens > 0);
        }

        [Fact]
        public void BuildLibraryAwarePrompt_Includes_Metadata_Sections()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var b = new LibraryAwarePromptBuilder(logger);
            var profile = new LibraryProfile
            {
                TotalArtists = 42,
                TotalAlbums = 100,
                TopGenres = new Dictionary<string, int> { ["rock"] = 10, ["jazz"] = 5 },
                TopArtists = new List<string> { "ArtistA", "ArtistB" },
                RecentlyAdded = new List<string> { "New1", "New2" },
                Metadata = new Dictionary<string, object>
                {
                    ["CollectionSize"] = "large",
                    ["GenreDistribution"] = new Dictionary<string, double> { ["rock"] = 60.0, ["jazz"] = 40.0 },
                    ["CollectionStyle"] = "curated",
                    ["CompletionistScore"] = 0.75,
                    ["AverageAlbumsPerArtist"] = 3.2,
                    ["ReleaseDecades"] = new List<string> { "1970s", "1980s" },
                    ["PreferredEras"] = new List<string> { "1990s", "2000s" },
                    ["AlbumTypes"] = new Dictionary<string, int> { ["studio"] = 80, ["live"] = 5 },
                    ["NewReleaseRatio"] = 0.2,
                    ["DiscoveryTrend"] = "steady",
                    ["CollectionCompleteness"] = 0.65,
                    ["MonitoredRatio"] = 0.9,
                    ["TopCollectedArtistNames"] = new Dictionary<string, int> { ["ArtistA"] = 10, ["ArtistB"] = 8 }
                }
            };
            var prompt = b.BuildLibraryAwarePrompt(profile, new List<Artist>(), new List<Album>(), new BrainarrSettings { SamplingStrategy = SamplingStrategy.Balanced });

            Assert.Contains("COLLECTION OVERVIEW:", prompt);
            Assert.Contains("MUSICAL DNA:", prompt);
            Assert.Contains("COLLECTION PATTERNS:", prompt);
            Assert.Contains("Recently added artists:", prompt);
            Assert.Contains("(completionist score:", prompt);
            Assert.Contains("Collection quality:", prompt);
            Assert.Contains("Active tracking:", prompt);
            Assert.Contains("Top collected artists:", prompt);
        }
    }
}
