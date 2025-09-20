using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
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
        public void ComputeSamplingSeed_IsStableAcrossInstances()
        {
            var builder1 = new LibraryAwarePromptBuilder(Logger);
            var builder2 = new LibraryAwarePromptBuilder(Logger);
            var profile1 = MakeProfile(artists: 150, albums: 320);
            var profile2 = MakeProfile(artists: 150, albums: 320);
            profile1.Metadata["PreferredEras"] = new List<string> { "1990s", "2000s" };
            profile2.Metadata["PreferredEras"] = new List<string> { "1990s", "2000s" };
            var settings = MakeSettings(AIProvider.Ollama, SamplingStrategy.Balanced, DiscoveryMode.Exploratory, max: 8);

            var seed1 = builder1.ComputeSamplingSeed(profile1, settings, shouldRecommendArtists: false);
            var seed2 = builder2.ComputeSamplingSeed(profile2, settings, shouldRecommendArtists: false);

            Assert.Equal(seed1, seed2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void ComputeSamplingSeed_IgnoresOrderingForEquivalentInputs()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var settings = MakeSettings(AIProvider.OpenAI, SamplingStrategy.Balanced, DiscoveryMode.Adjacent, max: 12);

            var profileA = MakeProfile(artists: 75, albums: 180);
            var profileB = MakeProfile(artists: 75, albums: 180);

            profileA.TopArtists.Clear();
            profileA.TopArtists.AddRange(new[] { "Zeta Artist", "Alpha Artist", "Gamma Artist" });
            profileB.TopArtists.Clear();
            profileB.TopArtists.AddRange(new[] { "Gamma Artist", "Alpha Artist", "Zeta Artist" });

            profileA.RecentlyAdded.AddRange(new[] { "Newcomer A", "Newcomer B", "Newcomer C" });
            profileB.RecentlyAdded.AddRange(new[] { "Newcomer C", "Newcomer B", "Newcomer A" });

            profileA.Metadata["PreferredEras"] = new List<string> { "1970s", "1990s", "2000s" };
            profileB.Metadata["PreferredEras"] = new List<string> { "2000s", "1970s", "1990s" };

            profileA.Metadata["TasteClusters"] = new List<object>
            {
                new[] { "Dream Pop", "Shoegaze" },
                new HashSet<string> { "Indie", "Alternative" }
            };
            profileB.Metadata["TasteClusters"] = new List<object>
            {
                new HashSet<string> { "Alternative", "Indie" },
                new[] { "Shoegaze", "Dream Pop" }
            };

            var seedA = builder.ComputeSamplingSeed(profileA, settings, shouldRecommendArtists: false);
            var seedB = builder.ComputeSamplingSeed(profileB, settings, shouldRecommendArtists: false);

            Assert.Equal(seedA, seedB);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void ComputeSamplingSeed_TreatsEquivalentNestedMetadataConsistently()
        {
            var builder = new LibraryAwarePromptBuilder(Logger);
            var settings = MakeSettings(AIProvider.LMStudio, SamplingStrategy.Comprehensive, DiscoveryMode.Similar, max: 5);

            var listProfile = MakeProfile(artists: 30, albums: 90);
            var setProfile = MakeProfile(artists: 30, albums: 90);

            listProfile.Metadata["ListeningModes"] = new List<object>
            {
                new[] { "Focus", "Relax" },
                new Dictionary<string, object>
                {
                    { "Weekday", new[] { "Morning", "Evening" } },
                    { "Weekend", new[] { "Afternoon", "Night" } }
                }
            };

            setProfile.Metadata["ListeningModes"] = new List<object>
            {
                new HashSet<string> { "Relax", "Focus" },
                new Dictionary<string, object>
                {
                    { "Weekend", new[] { "Night", "Afternoon" } },
                    { "Weekday", new HashSet<string> { "Evening", "Morning" } }
                }
            };

            var seedList = builder.ComputeSamplingSeed(listProfile, settings, shouldRecommendArtists: false);
            var seedSet = builder.ComputeSamplingSeed(setProfile, settings, shouldRecommendArtists: false);

            Assert.Equal(seedList, seedSet);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void ComputeStableHash_MasksHighBitToKeepSeedNonNegative()
        {
            var result = LibraryAwarePromptBuilder.ComputeStableHash(new[] { "hello" });

            Assert.Equal(1, result.ComponentCount);
            Assert.Equal("2cf24dba", result.HashPrefix);
            Assert.Equal(978182700, result.Seed);
        }
    }
}
