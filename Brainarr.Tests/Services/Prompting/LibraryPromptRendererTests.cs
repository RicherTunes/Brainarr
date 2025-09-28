using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.Music;

namespace Brainarr.Tests.Services.Prompting
{
    public class LibraryPromptRendererTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptRenderer")]
        public void Render_BuildsPromptWithAnchors()

        {
            var sample = new LibrarySample();
            sample.Artists.Add(new LibrarySampleArtist
            {
                ArtistId = 1,
                Name = "ArtistA",
                MatchedStyles = new[] { "shoegaze" },
                Weight = 1.0,
            });
            sample.Artists[0].Albums.Add(new LibrarySampleAlbum
            {
                AlbumId = 10,
                ArtistId = 1,
                ArtistName = "ArtistA",
                Title = "AlbumA",
                MatchedStyles = new[] { "shoegaze" },
                Added = DateTime.UtcNow.AddDays(-30),
                Year = DateTime.UtcNow.Year - 1
            });

            sample.Albums.Add(new LibrarySampleAlbum
            {
                AlbumId = 20,
                ArtistId = 2,
                ArtistName = "ArtistB",
                Title = "AlbumB",
                MatchedStyles = new[] { "dreampop" },
                Added = DateTime.UtcNow.AddDays(-10),
                Year = DateTime.UtcNow.Year - 2
            });

            var styleContext = new StylePlanContext(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze", "dreampop" },
                new List<StyleEntry> { new() { Name = "Shoegaze", Slug = "shoegaze" } },
                new List<StyleEntry> { new() { Name = "Dream Pop", Slug = "dreampop" } },
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["shoegaze"] = 4,
                    ["dreampop"] = 2
                },
                relaxed: true,
                threshold: 0.75,
                trimmed: new List<string>(),
                inferred: new List<string>());

            var plan = new PromptPlan(sample, new[] { "shoegaze" })
            {
                Profile = new LibraryProfile
                {
                    TotalArtists = 10,
                    TotalAlbums = 25,
                    TopArtists = new List<string> { "ArtistA", "ArtistB" },
                    TopGenres = new Dictionary<string, int> { ["shoegaze"] = 5, ["dreampop"] = 4 },
                    Metadata = new Dictionary<string, object>(),
                    StyleContext = new LibraryStyleContext()
                },
                Settings = new BrainarrSettings
                {
                    DiscoveryMode = DiscoveryMode.Adjacent,
                    SamplingStrategy = SamplingStrategy.Balanced,
                    MaxRecommendations = 5
                },
                StyleContext = styleContext,
                ShouldRecommendArtists = false,
                Compression = new PromptCompressionState(maxArtists: 5, maxAlbumGroups: 4, maxAlbumsPerGroup: 3)
            };

            var renderer = new LibraryPromptRenderer();
            var prompt = renderer.Render(plan, ModelPromptTemplate.Default, CancellationToken.None);

            Assert.Contains("[STYLE_AWARE] Use balanced sampling with key artists/albums.", prompt, StringComparison.Ordinal);
            Assert.Contains("🎯 RECOMMENDATION REQUIREMENTS:", prompt, StringComparison.Ordinal);
            Assert.Contains("Dream Pop", prompt, StringComparison.Ordinal);
            Assert.Contains("LIBRARY ARTISTS & KEY ALBUMS", prompt, StringComparison.Ordinal);
            Assert.Contains("JSON Response Format:", prompt, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptRenderer")]
        public void Render_AnthropicTemplateAddsStrictJsonInstruction()
        {
            var plan = CreateMinimalPlan();
            var renderer = new LibraryPromptRenderer();

            var prompt = renderer.Render(plan, ModelPromptTemplate.Anthropic, CancellationToken.None);

            Assert.Contains("Respond with a single JSON array", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("JSON Response Format:", prompt, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptRenderer")]
        public void Render_GeminiTemplateAddsJsonOnlyInstruction()
        {
            var plan = CreateMinimalPlan();
            var renderer = new LibraryPromptRenderer();

            var prompt = renderer.Render(plan, ModelPromptTemplate.Gemini, CancellationToken.None);

            Assert.Contains("Respond using application/json only", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("JSON Response Format:", prompt, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptRenderer")]
        public void Render_StyleFiltersListIsDeterministic()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var styleCatalog = new TestStyleCatalog(
                new StyleEntry { Name = "Shoegaze", Slug = "shoegaze" },
                new StyleEntry { Name = "Dream Pop", Slug = "dreampop" },
                new StyleEntry { Name = "Alt Rock", Slug = "alt" });
            var planner = new LibraryPromptPlanner(logger, styleCatalog, planCache: null);

            var context = new LibraryStyleContext();
            context.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["alt"] = 4,
                ["shoegaze"] = 3,
                ["dreampop"] = 2
            });

            context.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 1, 2 },
                    ["shoegaze"] = new[] { 2, 3 },
                    ["dreampop"] = new[] { 3 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alt"] = new[] { 11, 21 },
                    ["shoegaze"] = new[] { 22, 31 },
                    ["dreampop"] = new[] { 32 }
                }));

            context.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.ArtistStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt", "shoegaze" };
            context.ArtistStyles[3] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze", "dreampop" };

            context.AlbumStyles[11] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.AlbumStyles[21] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alt" };
            context.AlbumStyles[22] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" };
            context.AlbumStyles[31] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shoegaze" };
            context.AlbumStyles[32] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dreampop" };

            var profile = new LibraryProfile
            {
                TotalArtists = 3,
                TotalAlbums = 5,
                StyleContext = context
            };

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "ArtistA", Added = DateTime.UtcNow.AddDays(-10) },
                new Artist { Id = 2, Name = "ArtistB", Added = DateTime.UtcNow.AddDays(-6) },
                new Artist { Id = 3, Name = "ArtistC", Added = DateTime.UtcNow.AddDays(-3) }
            };

            var albums = new List<Album>
            {
                new Album { Id = 11, ArtistId = 1, Title = "Alt Album", Added = DateTime.UtcNow.AddDays(-20), ReleaseDate = DateTime.UtcNow.AddYears(-4) },
                new Album { Id = 21, ArtistId = 2, Title = "Alt Companion", Added = DateTime.UtcNow.AddDays(-18), ReleaseDate = DateTime.UtcNow.AddYears(-3) },
                new Album { Id = 22, ArtistId = 2, Title = "Shoegaze Entry", Added = DateTime.UtcNow.AddDays(-15), ReleaseDate = DateTime.UtcNow.AddYears(-2) },
                new Album { Id = 31, ArtistId = 3, Title = "Dream Sequence", Added = DateTime.UtcNow.AddDays(-12), ReleaseDate = DateTime.UtcNow.AddYears(-1) },
                new Album { Id = 32, ArtistId = 3, Title = "Night Bloom", Added = DateTime.UtcNow.AddDays(-11), ReleaseDate = DateTime.UtcNow.AddYears(-1) }
            };

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 5,
                StyleFilters = new[] { "shoegaze", "Alt", "dreampop" },
                MaxSelectedStyles = 5
            };

            var request = new RecommendationRequest(
                artists,
                albums,
                settings,
                context,
                recommendArtists: false,
                targetTokens: 3600,
                availableSamplingTokens: 2400,
                modelKey: "openai:gpt-4",
                contextWindow: 64000);

            var plan = planner.Plan(profile, request, CancellationToken.None);
            var renderer = new LibraryPromptRenderer();
            var prompt = renderer.Render(plan, ModelPromptTemplate.Default, CancellationToken.None);

            var lines = prompt.Split('\n');
            var headerIndex = Array.FindIndex(lines, line => line.Contains("🎨 STYLE FILTERS", StringComparison.Ordinal));
            Assert.True(headerIndex >= 0, "Style filters section not found.");

            var bulletNames = new List<string>();
            for (var i = headerIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }

                if (!line.StartsWith("• ", StringComparison.Ordinal))
                {
                    break;
                }

                var nameSegment = line.Substring(2);
                var coverageIndex = nameSegment.IndexOf("• coverage", StringComparison.Ordinal);
                if (coverageIndex >= 0)
                {
                    nameSegment = nameSegment.Substring(0, coverageIndex).TrimEnd();
                }

                var aliasIndex = nameSegment.IndexOf(" (aliases:", StringComparison.Ordinal);
                if (aliasIndex >= 0)
                {
                    nameSegment = nameSegment.Substring(0, aliasIndex).TrimEnd();
                }

                bulletNames.Add(nameSegment);
            }

            Assert.Equal(new[] { "Alt Rock", "Dream Pop", "Shoegaze" }, bulletNames);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptRenderer")]
        public void Render_TiedArtistsAndAlbums_OrderById()
        {
            var sample = new LibrarySample();
            var first = new LibrarySampleArtist
            {
                ArtistId = 7,
                Name = "Echo",
                Weight = 5.0,
                MatchedStyles = Array.Empty<string>()
            };
            first.Albums.Add(new LibrarySampleAlbum
            {
                AlbumId = 302,
                Title = "Resonance",
                Added = DateTime.UtcNow.AddDays(-10),
                Year = 2008
            });
            first.Albums.Add(new LibrarySampleAlbum
            {
                AlbumId = 301,
                Title = "Resonance",
                Added = DateTime.UtcNow.AddDays(-10),
                Year = 2005
            });

            var second = new LibrarySampleArtist
            {
                ArtistId = 3,
                Name = "Echo",
                Weight = 5.0,
                MatchedStyles = Array.Empty<string>()
            };
            second.Albums.Add(new LibrarySampleAlbum
            {
                AlbumId = 501,
                Title = "Signal",
                Added = DateTime.UtcNow.AddDays(-8),
                Year = 2011
            });
            second.Albums.Add(new LibrarySampleAlbum
            {
                AlbumId = 500,
                Title = "Signal",
                Added = DateTime.UtcNow.AddDays(-8),
                Year = 2009
            });

            sample.Artists.Add(first);
            sample.Artists.Add(second);

            var plan = new PromptPlan(sample, Array.Empty<string>())
            {
                Profile = new LibraryProfile(),
                Settings = new BrainarrSettings
                {
                    MaxRecommendations = 3,
                    DiscoveryMode = DiscoveryMode.Similar,
                    SamplingStrategy = SamplingStrategy.Balanced
                },
                Compression = new PromptCompressionState(maxArtists: 5, maxAlbumGroups: 5, maxAlbumsPerGroup: 1),
                ShouldRecommendArtists = false
            };

            var renderer = new LibraryPromptRenderer();
            var prompt = renderer.Render(plan, ModelPromptTemplate.Default, CancellationToken.None);

            var lines = prompt.Split('\n');
            var headerIndex = Array.FindIndex(lines, line => line.Contains("LIBRARY ARTISTS & KEY ALBUMS", StringComparison.Ordinal));
            Assert.True(headerIndex >= 0, "Artist section not found");

            var artistLines = new List<string>();
            for (var i = headerIndex + 1; i < lines.Length && lines[i].StartsWith("• ", StringComparison.Ordinal); i++)
            {
                artistLines.Add(lines[i]);
            }

            Assert.Equal(2, artistLines.Count);
            Assert.Contains("Echo", artistLines[0], StringComparison.Ordinal);
            Assert.Contains("Echo", artistLines[1], StringComparison.Ordinal);

            // Expect artist id 3 before id 7 when weight/name tie
            Assert.Contains("Signal", artistLines[0], StringComparison.Ordinal);
            Assert.Contains("Resonance", artistLines[1], StringComparison.Ordinal);

            // Albums with identical titles should surface the lower-id (earlier year) entry
            Assert.Contains("Signal (2009)", artistLines[0], StringComparison.Ordinal);
            Assert.Contains("Resonance (2005)", artistLines[1], StringComparison.Ordinal);
        }

        private static PromptPlan CreateMinimalPlan(bool recommendArtists = false)
        {
            var sample = new LibrarySample();
            return new PromptPlan(sample, Array.Empty<string>())
            {
                Profile = new LibraryProfile(),
                Settings = new BrainarrSettings
                {
                    MaxRecommendations = 3,
                    DiscoveryMode = DiscoveryMode.Adjacent,
                    SamplingStrategy = SamplingStrategy.Balanced
                },
                StyleContext = StylePlanContext.Empty,
                Compression = new PromptCompressionState(maxArtists: 5, maxAlbumGroups: 5, maxAlbumsPerGroup: 5),
                ShouldRecommendArtists = recommendArtists
            };
        }

        private sealed class TestStyleCatalog : IStyleCatalogService
        {
            private readonly Dictionary<string, StyleEntry> _entries;

            public TestStyleCatalog(params StyleEntry[] entries)
            {
                _entries = entries.ToDictionary(e => e.Slug, StringComparer.OrdinalIgnoreCase);
            }

            public IReadOnlyList<StyleEntry> GetAll() => _entries.Values.ToList();

            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => _entries.Values;

            public ISet<string> Normalize(IEnumerable<string> selected)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (selected == null)
                {
                    return set;
                }

                foreach (var value in selected)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var candidate = value.Trim();
                    if (_entries.TryGetValue(candidate, out var entry))
                    {
                        set.Add(entry.Slug);
                        continue;
                    }

                    var match = _entries.Values.FirstOrDefault(e =>
                        string.Equals(e.Name, candidate, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        set.Add(match.Slug);
                    }
                    else
                    {
                        set.Add(candidate);
                    }
                }

                return set;
            }

            public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs) => false;

            public string? ResolveSlug(string value) => value;

            public StyleEntry? GetBySlug(string slug) => _entries.TryGetValue(slug, out var entry) ? entry : null;

            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug) => Array.Empty<StyleSimilarity>();
        }
    }
}
