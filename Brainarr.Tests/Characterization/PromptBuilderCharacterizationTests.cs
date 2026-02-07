using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Characterization
{
    /// <summary>
    /// M6-1: Characterization tests locking LibraryAwarePromptBuilder behavior at public seams.
    /// These tests capture current behavior so that god-class extractions in M6-2 can be
    /// verified as behavior-preserving. Assertions are structured (field-by-field), not string snapshots.
    /// </summary>
    [Trait("Category", "Characterization")]
    [Trait("Area", "PromptBuilder")]
    public class PromptBuilderCharacterizationTests
    {
        private static readonly Logger Logger = LogManager.CreateNullLogger();

        // ─── BuildLibraryAwarePromptWithMetrics: All fields populated ──────────

        [Fact]
        public void WithMetrics_SmallLibrary_PopulatesAllResultFields()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 20, albums: 50);
            var settings = MakeSettings(AIProvider.Ollama, SamplingStrategy.Balanced);

            var result = builder.BuildLibraryAwarePromptWithMetrics(
                profile, MakeArtists(20), MakeAlbums(50, 20), settings);

            // Prompt should be non-empty
            result.Prompt.Should().NotBeNullOrWhiteSpace();

            // Sampling metrics should be populated
            result.SampledArtists.Should().BeGreaterThan(0);
            result.SampledAlbums.Should().BeGreaterOrEqualTo(0);
            result.EstimatedTokens.Should().BeGreaterThan(0);

            // Budget fields should be positive
            result.PromptBudgetTokens.Should().BeGreaterThan(0);
            result.ModelContextTokens.Should().BeGreaterThan(0);
            result.TokenHeadroom.Should().BeGreaterThan(0);
            result.BudgetModelKey.Should().NotBeNullOrWhiteSpace();

            // Seed and fingerprint should be populated
            result.SampleSeed.Should().NotBeNullOrWhiteSpace();
            result.SampleFingerprint.Should().NotBeNullOrWhiteSpace();

            // Style collections should be initialized (possibly empty for minimal profile)
            result.AppliedStyleSlugs.Should().NotBeNull();
            result.AppliedStyleNames.Should().NotBeNull();
            result.TrimmedStyles.Should().NotBeNull();
            result.InferredStyleSlugs.Should().NotBeNull();
            result.StyleCoverage.Should().NotBeNull();
            result.MatchedStyleCounts.Should().NotBeNull();

            // Token estimate should stay within budget
            result.EstimatedTokens.Should().BeLessOrEqualTo(
                result.ModelContextTokens - result.TokenHeadroom,
                "prompt must fit within (context - headroom)");
        }

        [Fact]
        public void WithMetrics_LargeLibrary_RespectsTokenBudget()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 500, albums: 1500);
            var settings = MakeSettings(AIProvider.OpenAI, SamplingStrategy.Comprehensive);

            var result = builder.BuildLibraryAwarePromptWithMetrics(
                profile, MakeArtists(500), MakeAlbums(1500, 500), settings);

            result.Prompt.Should().NotBeNullOrWhiteSpace();

            // Large library should sample, not include everything
            result.SampledArtists.Should().BeLessThan(500);

            // Token estimate must stay within model context minus headroom
            result.EstimatedTokens.Should().BeLessOrEqualTo(
                result.ModelContextTokens - result.TokenHeadroom);
        }

        // ─── GetEffectiveTokenLimit: Provider-Strategy matrix ──────────────────

        [Theory]
        [InlineData(AIProvider.Ollama, SamplingStrategy.Minimal)]
        [InlineData(AIProvider.Ollama, SamplingStrategy.Balanced)]
        [InlineData(AIProvider.Ollama, SamplingStrategy.Comprehensive)]
        [InlineData(AIProvider.OpenAI, SamplingStrategy.Minimal)]
        [InlineData(AIProvider.OpenAI, SamplingStrategy.Balanced)]
        [InlineData(AIProvider.OpenAI, SamplingStrategy.Comprehensive)]
        [InlineData(AIProvider.Anthropic, SamplingStrategy.Balanced)]
        [InlineData(AIProvider.Gemini, SamplingStrategy.Balanced)]
        [InlineData(AIProvider.DeepSeek, SamplingStrategy.Balanced)]
        [InlineData(AIProvider.Groq, SamplingStrategy.Balanced)]
        [InlineData(AIProvider.Perplexity, SamplingStrategy.Balanced)]
        [InlineData(AIProvider.OpenRouter, SamplingStrategy.Balanced)]
        public void GetEffectiveTokenLimit_AllProviders_ReturnsPositiveValue(AIProvider provider, SamplingStrategy strategy)
        {
            var builder = new LibraryAwarePromptBuilder(Logger);

            var limit = builder.GetEffectiveTokenLimit(strategy, provider);

            limit.Should().BeGreaterThan(0, $"{provider}/{strategy} should have a positive token limit");
            limit.Should().BeLessThan(500_000, "no provider should exceed 500K tokens");
        }

        [Fact]
        public void GetEffectiveTokenLimit_ComprehensiveAlwaysGTE_Minimal()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var providers = new[] { AIProvider.Ollama, AIProvider.OpenAI, AIProvider.Anthropic, AIProvider.Gemini };

            foreach (var provider in providers)
            {
                var minimal = builder.GetEffectiveTokenLimit(SamplingStrategy.Minimal, provider);
                var comprehensive = builder.GetEffectiveTokenLimit(SamplingStrategy.Comprehensive, provider);

                comprehensive.Should().BeGreaterOrEqualTo(minimal,
                    $"Comprehensive should >= Minimal for {provider}");
            }
        }

        // ─── EstimateTokens: boundary behavior ────────────────────────────────

        [Theory]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        public void EstimateTokens_EmptyOrNull_ReturnsZero(string input, int expected)
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            builder.EstimateTokens(input).Should().Be(expected);
        }

        [Fact]
        public void EstimateTokens_LongerText_ReturnsMoreTokens()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var short_ = builder.EstimateTokens("hello world");
            var long_ = builder.EstimateTokens("hello world this is a much longer text with many more words that should result in a higher token count");

            long_.Should().BeGreaterThan(short_);
        }

        [Fact]
        public void EstimateTokens_RealisticText_ReturnsPositive()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            // Use realistic text — repeated single chars may compress to 1 token
            var text = "Recommend albums similar to OK Computer by Radiohead. " +
                       "I enjoy alternative rock, post-rock, and electronic music. " +
                       "My library has 200 artists and 500 albums spanning genres like " +
                       "jazz, classical, indie, shoegaze, and ambient.";
            var tokens = builder.EstimateTokens(text);

            tokens.Should().BeGreaterThan(0, "realistic text should produce positive token count");
        }

        // ─── ComputeSamplingSeed: determinism ──────────────────────────────────

        [Fact]
        public void ComputeSamplingSeed_SameInputs_ReturnsSameSeed()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 50, albums: 100);
            var settings = MakeSettings(AIProvider.OpenAI, SamplingStrategy.Balanced);

            var seed1 = builder.ComputeSamplingSeed(profile, settings, shouldRecommendArtists: false);
            var seed2 = builder.ComputeSamplingSeed(profile, settings, shouldRecommendArtists: false);

            seed1.Should().Be(seed2, "same inputs must produce the same seed");
        }

        [Fact]
        public void ComputeSamplingSeed_DifferentProvider_ReturnsDifferentSeed()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 50, albums: 100);

            var seedOpenAI = builder.ComputeSamplingSeed(
                profile, MakeSettings(AIProvider.OpenAI), shouldRecommendArtists: false);
            var seedAnthropic = builder.ComputeSamplingSeed(
                profile, MakeSettings(AIProvider.Anthropic), shouldRecommendArtists: false);

            seedOpenAI.Should().NotBe(seedAnthropic, "different providers should produce different seeds");
        }

        [Fact]
        public void ComputeSamplingSeed_DifferentMode_ReturnsDifferentSeed()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 50, albums: 100);
            var settings = MakeSettings(AIProvider.OpenAI, SamplingStrategy.Balanced);

            var seedAlbum = builder.ComputeSamplingSeed(profile, settings, shouldRecommendArtists: false);
            var seedArtist = builder.ComputeSamplingSeed(profile, settings, shouldRecommendArtists: true);

            seedAlbum.Should().NotBe(seedArtist, "artist vs album mode should produce different seeds");
        }

        [Fact]
        public void ComputeSamplingSeed_NullProfile_Throws()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var settings = MakeSettings();

            var act = () => builder.ComputeSamplingSeed(null, settings, false);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void ComputeSamplingSeed_NullSettings_Throws()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 10, albums: 20);

            var act = () => builder.ComputeSamplingSeed(profile, null, false);

            act.Should().Throw<ArgumentNullException>();
        }

        // ─── BuildLibraryAwarePrompt: prompt shape contracts ───────────────────

        [Fact]
        public void BuildPrompt_ContainsExpectedSections()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 30, albums: 80);
            var settings = MakeSettings(AIProvider.Ollama, SamplingStrategy.Balanced);

            var prompt = builder.BuildLibraryAwarePrompt(
                profile, MakeArtists(30), MakeAlbums(80, 30), settings);

            // Core sections that should be present in any library-aware prompt
            prompt.Should().Contain("COLLECTION OVERVIEW", "prompt must contain collection overview");
            prompt.Should().Contain("RECOMMENDATION REQUIREMENTS", "prompt must contain recommendation requirements");
        }

        [Fact]
        public void BuildPrompt_ArtistMode_DiffersFromAlbumMode()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 30, albums: 80);
            var artists = MakeArtists(30);
            var albums = MakeAlbums(80, 30);
            var settings = MakeSettings(AIProvider.Ollama, SamplingStrategy.Balanced);

            var albumPrompt = builder.BuildLibraryAwarePrompt(profile, artists, albums, settings, shouldRecommendArtists: false);
            // New builder instance to avoid plan cache
            var builder2 = new LibraryAwarePromptBuilder(Logger);
            var artistPrompt = builder2.BuildLibraryAwarePrompt(profile, artists, albums, settings, shouldRecommendArtists: true);

            albumPrompt.Should().NotBe(artistPrompt, "artist mode and album mode should produce different prompts");
        }

        [Fact]
        public void BuildPrompt_NullArtistsAndAlbums_FallsBackGracefully()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 5, albums: 10);
            var settings = MakeSettings();

            var prompt = builder.BuildLibraryAwarePrompt(profile, null, null, settings);

            prompt.Should().NotBeNullOrWhiteSpace("null lists should produce a fallback prompt, not null/empty");
        }

        [Fact]
        public void BuildPrompt_EmptyProfile_FallsBackGracefully()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = new LibraryProfile();
            var settings = MakeSettings();

            var prompt = builder.BuildLibraryAwarePrompt(
                profile, new List<Artist>(), new List<Album>(), settings);

            prompt.Should().NotBeNullOrWhiteSpace("empty profile should produce a fallback prompt");
        }

        // ─── Determinism: same builder instance produces identical results ────

        [Fact]
        public void BuildPromptWithMetrics_SecondCall_UsesPlanCache()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 40, albums: 100);
            var artists = MakeArtists(40);
            var albums = MakeAlbums(100, 40);
            var settings = MakeSettings(AIProvider.Ollama, SamplingStrategy.Balanced);

            var first = builder.BuildLibraryAwarePromptWithMetrics(profile, artists, albums, settings);
            var second = builder.BuildLibraryAwarePromptWithMetrics(profile, artists, albums, settings);

            first.PlanCacheHit.Should().BeFalse("first call should not be a cache hit");
            second.PlanCacheHit.Should().BeTrue("second call with same inputs should hit plan cache");
            first.Prompt.Should().Be(second.Prompt, "deterministic builder should produce identical prompts");
            first.SampleFingerprint.Should().Be(second.SampleFingerprint);
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private static BrainarrSettings MakeSettings(
            AIProvider provider = AIProvider.Ollama,
            SamplingStrategy strategy = SamplingStrategy.Balanced,
            DiscoveryMode discovery = DiscoveryMode.Adjacent,
            int max = 10)
        {
            return new BrainarrSettings
            {
                Provider = provider,
                SamplingStrategy = strategy,
                DiscoveryMode = discovery,
                MaxRecommendations = max
            };
        }

        private static LibraryProfile MakeProfile(int artists, int albums)
        {
            return new LibraryProfile
            {
                TotalArtists = artists,
                TotalAlbums = albums,
                TopGenres = new Dictionary<string, int> { ["Rock"] = 50, ["Jazz"] = 30 },
                TopArtists = Enumerable.Range(1, Math.Min(10, artists)).Select(i => $"Artist{i}").ToList(),
                RecentlyAdded = new List<string> { "Artist1", "Artist2" },
                Metadata = new Dictionary<string, object>
                {
                    ["CollectionSize"] = "established",
                    ["CollectionFocus"] = "general",
                    ["DiscoveryTrend"] = "steady"
                },
                StyleContext = new LibraryStyleContext()
            };
        }

        private static List<Artist> MakeArtists(int count)
        {
            return Enumerable.Range(1, count).Select(i => new Artist
            {
                Id = i,
                Name = $"Artist{i}",
                Added = DateTime.UtcNow.AddDays(-i)
            }).ToList();
        }

        private static List<Album> MakeAlbums(int count, int artistCount)
        {
            return Enumerable.Range(1, count).Select(i =>
            {
                var artistId = ((i - 1) % Math.Max(1, artistCount)) + 1;
                return new Album
                {
                    ArtistId = artistId,
                    Title = $"Album{i}",
                    Added = DateTime.UtcNow.AddDays(-i),
                    ArtistMetadata = new ArtistMetadata { Name = $"Artist{artistId}" }
                };
            }).ToList();
        }
    }
}
