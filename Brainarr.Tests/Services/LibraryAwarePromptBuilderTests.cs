using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class LibraryAwarePromptBuilderTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static BrainarrSettings MakeSettings(
            AIProvider provider = AIProvider.Ollama,
            SamplingStrategy sampling = SamplingStrategy.Balanced,
            DiscoveryMode discovery = DiscoveryMode.Adjacent,
            int max = 10)
        {
            return new BrainarrSettings
            {
                Provider = provider,
                SamplingStrategy = sampling,
                DiscoveryMode = discovery,
                MaxRecommendations = max
            };
        }

        private static LibraryProfile MakeProfile(int artists, int albums)
        {
            return new LibraryProfile
            {
                TopGenres = new Dictionary<string, int> { { "Rock", 50 }, { "Metal", 30 } },
                TopArtists = Enumerable.Range(1, Math.Min(10, artists)).Select(i => $"Artist{i}").ToList(),
                TotalAlbums = albums,
                TotalArtists = artists,
                Metadata = new Dictionary<string, object>
                {
                    { "CollectionSize", "established" },
                    { "CollectionFocus", "general" },
                    { "DiscoveryTrend", "steady" }
                }
            };
        }

        private static List<Artist> MakeArtists(int count)
        {
            var list = new List<Artist>(count);
            for (int i = 1; i <= count; i++)
            {
                list.Add(new Artist
                {
                    Id = i,
                    Name = $"Artist{i}",
                    Added = DateTime.UtcNow.AddDays(-i)
                });
            }
            return list;
        }

        private static List<Album> MakeAlbums(int count, int artists)
        {
            var list = new List<Album>(count);
            for (int i = 1; i <= count; i++)
            {
                var artistId = ((i - 1) % Math.Max(1, artists)) + 1;
                list.Add(new Album
                {
                    ArtistId = artistId,
                    Title = $"Album{i}",
                    Added = DateTime.UtcNow.AddDays(-i),
                    ArtistMetadata = new ArtistMetadata { Name = $"Artist{artistId}" }
                });
            }
            return list;
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void EffectiveTokenLimit_RespectsProviderAndStrategy()
        {
            var b = new LibraryAwarePromptBuilder(Logger);
            var minOllama = b.GetEffectiveTokenLimit(SamplingStrategy.Minimal, AIProvider.Ollama);
            var balCloud = b.GetEffectiveTokenLimit(SamplingStrategy.Balanced, AIProvider.OpenAI);
            var compLocal = b.GetEffectiveTokenLimit(SamplingStrategy.Comprehensive, AIProvider.LMStudio);

            Assert.True(minOllama > 0);
            Assert.True(compLocal > balCloud); // comprehensive(local) should exceed balanced(cloud)
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void SmallLibrary_BuildsPromptWithMetrics()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 20, albums: 50);
            var artists = MakeArtists(20);
            var albums = MakeAlbums(50, 20);
            var settings = MakeSettings(AIProvider.Ollama, SamplingStrategy.Balanced, DiscoveryMode.Adjacent, max: 10);

            var res = builder.BuildLibraryAwarePromptWithMetrics(profile, artists, albums, settings, shouldRecommendArtists: false);

            Assert.False(string.IsNullOrWhiteSpace(res.Prompt));
            Assert.InRange(res.SampledArtists, 1, 40);
            Assert.InRange(res.SampledAlbums, 1, 100);
            Assert.True(res.EstimatedTokens > 0);
            Assert.Contains("COLLECTION OVERVIEW", res.Prompt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void MediumLibrary_UsesStrategicSampling()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 120, albums: 200);
            var artists = MakeArtists(120);
            var albums = MakeAlbums(200, 120);
            var settings = MakeSettings(AIProvider.OpenAI, SamplingStrategy.Balanced, DiscoveryMode.Adjacent, max: 12);

            var res = builder.BuildLibraryAwarePromptWithMetrics(profile, artists, albums, settings, shouldRecommendArtists: false);

            Assert.True(res.SampledArtists <= 120);
            Assert.True(res.SampledAlbums <= 200);
            Assert.Contains("RECOMMENDATION REQUIREMENTS", res.Prompt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void LargeLibrary_RespectsTokenBudget()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var profile = MakeProfile(artists: 400, albums: 1200);
            var artists = MakeArtists(400);
            var albums = MakeAlbums(1200, 400);
            var settings = MakeSettings(AIProvider.Ollama, SamplingStrategy.Comprehensive, DiscoveryMode.Similar, max: 15);

            var res = builder.BuildLibraryAwarePromptWithMetrics(profile, artists, albums, settings, shouldRecommendArtists: true);
            var limit = builder.GetEffectiveTokenLimit(settings.SamplingStrategy, settings.Provider);
            var est = builder.EstimateTokens(res.Prompt);

            Assert.True(est <= limit, $"Estimated tokens {est} should be <= limit {limit}");
            Assert.True(res.SampledArtists + res.SampledAlbums > 0);
            Assert.Contains("LIBRARY ARTISTS", res.Prompt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void StyleSelection_NormalizesOrderAndDuplicates()
        {
            var selection = new LibraryStyleSelection(
                selectedSlugs: new[] { "Progressive-Rock", "progressive-rock", "Jazz-Fusion" },
                expandedSlugs: new[] { "jazz-fusion", "Avant-Prog", "PROGRESSIVE-ROCK" },
                relaxAdjacentStyles: true);

            Assert.Equal(new[] { "Progressive-Rock", "Jazz-Fusion" }, selection.SelectedSlugs);
            Assert.Equal(new[] { "Progressive-Rock", "Jazz-Fusion", "Avant-Prog" }, selection.ExpandedSlugs);
            Assert.True(selection.RelaxAdjacentStyles);
            Assert.True(selection.ShouldUseRelaxedMatches);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void StyleSelection_NoExpansion_DisablesRelaxation()
        {
            var selection = new LibraryStyleSelection(
                selectedSlugs: new[] { "progressive-rock" },
                expandedSlugs: new[] { "PROGRESSIVE-ROCK" },
                relaxAdjacentStyles: true);

            Assert.True(selection.RelaxAdjacentStyles);
            Assert.False(selection.ShouldUseRelaxedMatches);

            var builder = new LibraryAwarePromptBuilder(Logger);
            var styleIndex = new LibraryStyleIndex(
                new Dictionary<string, IEnumerable<int>>
                {
                    ["progressive-rock"] = new[] { 7, 9 }
                },
                new Dictionary<string, IEnumerable<int>>
                {
                    ["progressive-rock"] = new[] { 21 }
                });

            var matches = builder.BuildArtistMatchList(styleIndex, selection);
            Assert.Equal(new[] { 7, 9 }, matches.StrictMatches);
            Assert.Same(matches.StrictMatches, matches.RelaxedMatches);
            Assert.False(matches.HasRelaxedMatches);

            var albumMatches = builder.BuildAlbumMatchList(styleIndex, selection);
            Assert.Equal(new[] { 21 }, albumMatches.StrictMatches);
            Assert.Same(albumMatches.StrictMatches, albumMatches.RelaxedMatches);
            Assert.False(albumMatches.HasRelaxedMatches);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void StyleMatching_IsCaseInsensitive()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);

            var styleIndex = new LibraryStyleIndex(
                new Dictionary<string, IEnumerable<int>>
                {
                    ["progressive-rock"] = new[] { 1, 2, 3 },
                    ["jazz-fusion"] = new[] { 10 }
                },
                new Dictionary<string, IEnumerable<int>>
                {
                    ["progressive-rock"] = new[] { 20 },
                    ["jazz-fusion"] = new[] { 21 }
                });

            var selection = new LibraryStyleSelection(
                selectedSlugs: new[] { "ProgRessive-Rock", "progRessive-rock" },
                expandedSlugs: new[] { "ProgRessive-Rock", "JAZZ-FUSION" },
                relaxAdjacentStyles: true);

            var artistMatches = builder.BuildArtistMatchList(styleIndex, selection);
            Assert.Equal(new[] { 1, 2, 3, 10 }, artistMatches.RelaxedMatches);
            Assert.True(artistMatches.HasRelaxedMatches);

            var albumMatches = builder.BuildAlbumMatchList(styleIndex, selection);
            Assert.Equal(new[] { 20, 21 }, albumMatches.RelaxedMatches);
            Assert.True(albumMatches.HasRelaxedMatches);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void StyleMatching_RelaxedMode_ThrottlesInflatedResults()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);

            var strictIds = Enumerable.Range(1, 10).ToArray();
            var inflatedIds = Enumerable.Range(1000, 600).ToArray();

            var styleIndex = new LibraryStyleIndex(
                new Dictionary<string, IEnumerable<int>>
                {
                    ["primary"] = strictIds
                },
                new Dictionary<string, IEnumerable<int>>
                {
                    ["primary"] = strictIds
                });

            var expandedIndex = new LibraryStyleIndex(
                new Dictionary<string, IEnumerable<int>>
                {
                    ["primary"] = strictIds,
                    ["adjacent"] = inflatedIds
                },
                new Dictionary<string, IEnumerable<int>>
                {
                    ["primary"] = strictIds,
                    ["adjacent"] = inflatedIds
                });

            var relaxedSelection = new LibraryStyleSelection(
                selectedSlugs: new[] { "primary" },
                expandedSlugs: new[] { "primary", "adjacent" },
                relaxAdjacentStyles: true);

            var strictSelection = new LibraryStyleSelection(
                selectedSlugs: new[] { "primary" },
                expandedSlugs: new[] { "primary" },
                relaxAdjacentStyles: false);

            var strictMatches = builder.BuildArtistMatchList(styleIndex, strictSelection);
            Assert.Equal(strictIds, strictMatches.StrictMatches);
            Assert.False(strictMatches.HasRelaxedMatches);

            var relaxedMatches = builder.BuildArtistMatchList(expandedIndex, relaxedSelection);
            Assert.Equal(strictIds, relaxedMatches.StrictMatches);
            Assert.Same(relaxedMatches.StrictMatches, relaxedMatches.RelaxedMatches);
            Assert.False(relaxedMatches.HasRelaxedMatches);

            var relaxedAlbums = builder.BuildAlbumMatchList(expandedIndex, relaxedSelection);
            Assert.Equal(strictIds, relaxedAlbums.StrictMatches);
            Assert.Same(relaxedAlbums.StrictMatches, relaxedAlbums.RelaxedMatches);
            Assert.False(relaxedAlbums.HasRelaxedMatches);

            var truncatedAdjacent = expandedIndex.GetArtistMatches(new[] { "adjacent" });
            Assert.Equal(500, truncatedAdjacent.Count);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void StyleMatching_RelaxedMode_IncludesAdjacentStyles()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);

            var styleIndex = new LibraryStyleIndex(
                new Dictionary<string, IEnumerable<int>>
                {
                    ["progressive-rock"] = Array.Empty<int>(),
                    ["jazz-fusion"] = new[] { 11, 42 }
                },
                new Dictionary<string, IEnumerable<int>>
                {
                    ["progressive-rock"] = Array.Empty<int>(),
                    ["jazz-fusion"] = new[] { 101 }
                });

            var strictSelection = new LibraryStyleSelection(
                selectedSlugs: new[] { "progressive-rock" },
                expandedSlugs: new[] { "progressive-rock", "jazz-fusion" },
                relaxAdjacentStyles: false);

            var relaxedSelection = new LibraryStyleSelection(
                selectedSlugs: new[] { "progressive-rock" },
                expandedSlugs: new[] { "progressive-rock", "jazz-fusion" },
                relaxAdjacentStyles: true);

            var strictArtistMatches = builder.BuildArtistMatchList(styleIndex, strictSelection);
            Assert.Empty(strictArtistMatches.StrictMatches);
            Assert.Same(strictArtistMatches.StrictMatches, strictArtistMatches.RelaxedMatches);

            var relaxedArtistMatches = builder.BuildArtistMatchList(styleIndex, relaxedSelection);
            Assert.Empty(relaxedArtistMatches.StrictMatches);
            Assert.Equal(new[] { 11, 42 }, relaxedArtistMatches.RelaxedMatches);
            Assert.True(relaxedArtistMatches.HasRelaxedMatches);

            var strictAlbumMatches = builder.BuildAlbumMatchList(styleIndex, strictSelection);
            Assert.Empty(strictAlbumMatches.StrictMatches);
            Assert.Same(strictAlbumMatches.StrictMatches, strictAlbumMatches.RelaxedMatches);

            var relaxedAlbumMatches = builder.BuildAlbumMatchList(styleIndex, relaxedSelection);
            Assert.Empty(relaxedAlbumMatches.StrictMatches);
            Assert.Equal(new[] { 101 }, relaxedAlbumMatches.RelaxedMatches);
            Assert.True(relaxedAlbumMatches.HasRelaxedMatches);
        }
    }
}
