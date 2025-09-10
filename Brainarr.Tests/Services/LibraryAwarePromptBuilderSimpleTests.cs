using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

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
            var artists = new List<NzbDrone.Core.Music.Artist>();
            var albums = new List<NzbDrone.Core.Music.Album>();

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
    }
}
