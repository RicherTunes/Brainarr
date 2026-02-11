using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    /// <summary>
    /// Golden snapshot test for the prompt pipeline. Builds a deterministic fixture
    /// and asserts a stable JSON snapshot of sections + key metrics.
    /// Any structural change to the prompt output will break this test,
    /// providing a tight guardrail for future refactors.
    /// </summary>
    public class LibraryPromptRendererGoldenTests
    {
        /// <summary>
        /// Hermetic golden snapshot: fixed fixture → render → extract structural elements → compare JSON.
        /// Uses InvariantCulture to ensure P0/F1 format determinism across environments.
        /// </summary>
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void GoldenSnapshot_DefaultTemplate_StructureIsStable()
        {
            var savedCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                var plan = BuildGoldenFixture();
                var renderer = new LibraryPromptRenderer();
                var prompt = renderer.Render(plan, ModelPromptTemplate.Default, CancellationToken.None);
                var snapshot = ExtractSnapshot(prompt);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var actualJson = Normalize(JsonSerializer.Serialize(snapshot, options));

                Assert.Equal(ExpectedGoldenJson, actualJson);
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
            }
        }

        /// <summary>
        /// Verifies that the golden fixture renders identically across all three templates
        /// at the structural level (sections present, artist groups, recommendation type).
        /// Only the response format line differs.
        /// </summary>
        [Theory]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        [InlineData("default", "JSON Response Format:")]
        [InlineData("anthropic", "Respond with a single JSON array")]
        [InlineData("gemini", "Respond using application/json only")]
        public void GoldenSnapshot_AllTemplates_ShareStructure(string templateName, string expectedFormatMarker)
        {
            var savedCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                var template = templateName switch
                {
                    "anthropic" => ModelPromptTemplate.Anthropic,
                    "gemini" => ModelPromptTemplate.Gemini,
                    _ => ModelPromptTemplate.Default
                };

                var plan = BuildGoldenFixture();
                var renderer = new LibraryPromptRenderer();
                var prompt = renderer.Render(plan, template, CancellationToken.None);
                var snapshot = ExtractSnapshot(prompt);

                // All templates should render the same sections
                Assert.Equal(new[] { "COLLECTION OVERVIEW", "MUSICAL DNA", "COLLECTION PATTERNS" }, snapshot.SectionsRendered);
                Assert.Equal(2, snapshot.ArtistGroupCount);
                Assert.Contains(expectedFormatMarker, prompt, StringComparison.Ordinal);
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
            }
        }

        private static PromptPlan BuildGoldenFixture()
        {
            var sample = new LibrarySample();

            var pinkFloyd = new LibrarySampleArtist
            {
                ArtistId = 1,
                Name = "Pink Floyd",
                MatchedStyles = new[] { "progressive-rock" },
                Weight = 2.0,
                Added = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc)
            };
            pinkFloyd.Albums.Add(new LibrarySampleAlbum
            {
                AlbumId = 10,
                ArtistId = 1,
                ArtistName = "Pink Floyd",
                Title = "The Dark Side of the Moon",
                MatchedStyles = new[] { "progressive-rock" },
                Added = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                Year = 1973
            });

            var milesDavis = new LibrarySampleArtist
            {
                ArtistId = 2,
                Name = "Miles Davis",
                MatchedStyles = new[] { "jazz" },
                Weight = 1.5,
                Added = new DateTime(2024, 2, 10, 0, 0, 0, DateTimeKind.Utc)
            };
            milesDavis.Albums.Add(new LibrarySampleAlbum
            {
                AlbumId = 20,
                ArtistId = 2,
                ArtistName = "Miles Davis",
                Title = "Kind of Blue",
                MatchedStyles = new[] { "jazz" },
                Added = new DateTime(2024, 2, 10, 0, 0, 0, DateTimeKind.Utc),
                Year = 1959
            });

            sample.Artists.Add(pinkFloyd);
            sample.Artists.Add(milesDavis);
            sample.Albums.Add(pinkFloyd.Albums[0]);
            sample.Albums.Add(milesDavis.Albums[0]);

            var profile = new LibraryProfile
            {
                TotalArtists = 5,
                TotalAlbums = 12,
                TopGenres = new Dictionary<string, int>
                {
                    ["Rock"] = 8,
                    ["Jazz"] = 4
                },
                TopArtists = new List<string> { "Pink Floyd", "Miles Davis" },
                RecentlyAdded = new List<string> { "Radiohead", "Thelonious Monk" },
                Metadata = new Dictionary<string, object>
                {
                    ["CollectionSize"] = "moderate",
                    ["CollectionFocus"] = "deep-cuts",
                    ["GenreDistribution"] = new Dictionary<string, double>
                    {
                        ["Rock"] = 40.0,
                        ["Jazz"] = 25.0,
                        ["Electronic"] = 15.0
                    },
                    ["CollectionStyle"] = "curated",
                    ["CompletionistScore"] = 72.5,
                    ["AverageAlbumsPerArtist"] = 2.4,
                    ["PreferredEras"] = new List<string> { "Classic", "Modern" },
                    ["AlbumTypes"] = new Dictionary<string, int>
                    {
                        ["Studio"] = 10,
                        ["Live"] = 2
                    },
                    ["NewReleaseRatio"] = 0.25,
                    ["DiscoveryTrend"] = "accelerating",
                    ["CollectionCompleteness"] = 0.65,
                    ["MonitoredRatio"] = 0.80,
                    ["TopCollectedArtistNames"] = new Dictionary<string, int>
                    {
                        ["Pink Floyd"] = 4,
                        ["Miles Davis"] = 3
                    }
                },
                StyleContext = new LibraryStyleContext()
            };

            return new PromptPlan(sample, Array.Empty<string>())
            {
                Profile = profile,
                Settings = new BrainarrSettings
                {
                    DiscoveryMode = DiscoveryMode.Adjacent,
                    SamplingStrategy = SamplingStrategy.Balanced,
                    MaxRecommendations = 5,
                    RecommendationMode = RecommendationMode.SpecificAlbums
                },
                StyleContext = StylePlanContext.Empty,
                ShouldRecommendArtists = false,
                Compression = new PromptCompressionState(
                    maxArtists: 5,
                    maxAlbumGroups: 5,
                    maxAlbumsPerGroup: 3,
                    minAlbumsPerGroup: 2),
                SampleSeed = "golden-test-seed"
            };
        }

        private static PromptSnapshot ExtractSnapshot(string prompt)
        {
            var lines = prompt.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .ToArray();

            var sections = new List<string>();
            var sectionHeadings = new[]
            {
                "COLLECTION OVERVIEW", "MUSICAL DNA", "COLLECTION PATTERNS"
            };

            foreach (var heading in sectionHeadings)
            {
                if (lines.Any(l => l.Contains(heading, StringComparison.Ordinal)))
                {
                    sections.Add(heading);
                }
            }

            // Extract artist group lines
            var artistHeaderIdx = Array.FindIndex(lines,
                l => l.Contains("LIBRARY ARTISTS & KEY ALBUMS", StringComparison.Ordinal));

            var artistNames = new List<string>();
            if (artistHeaderIdx >= 0)
            {
                for (var i = artistHeaderIdx + 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (!line.StartsWith("\u2022 ", StringComparison.Ordinal))
                    {
                        break;
                    }

                    // Extract artist name: "• Pink Floyd [progressive-rock] — [...]"
                    var nameEnd = line.IndexOf(" [", 2, StringComparison.Ordinal);
                    var dashEnd = line.IndexOf(" \u2014", 2, StringComparison.Ordinal);
                    var end = nameEnd >= 0 ? nameEnd : dashEnd >= 0 ? dashEnd : line.Length;
                    artistNames.Add(line.Substring(2, end - 2));
                }
            }

            // Extract planner header
            var plannerLine = lines.FirstOrDefault(l =>
                l.StartsWith("[PLANNER]", StringComparison.Ordinal)) ?? "";

            // Extract sampling preamble
            var samplingLine = lines.FirstOrDefault(l =>
                l.StartsWith("[STYLE_AWARE]", StringComparison.Ordinal)) ?? "";

            // Detect recommendation type
            var recType = lines.Any(l => l.Contains("NEW ALBUM recommendations", StringComparison.Ordinal))
                ? "albums"
                : lines.Any(l => l.Contains("NEW ARTIST recommendations", StringComparison.Ordinal))
                    ? "artists"
                    : "unknown";

            // Detect response format
            var formatLine = lines.Any(l => l.Contains("JSON Response Format:", StringComparison.Ordinal))
                ? "default"
                : lines.Any(l => l.Contains("Respond with a single JSON array", StringComparison.Ordinal))
                    ? "anthropic"
                    : lines.Any(l => l.Contains("Respond using application/json only", StringComparison.Ordinal))
                        ? "gemini"
                        : "unknown";

            // Extract collection character/temporal/discovery from requirements
            var characterLine = lines.FirstOrDefault(l =>
                l.Contains("collection's", StringComparison.Ordinal) && l.Contains("character", StringComparison.Ordinal));
            var collectionCharacter = ExtractBetween(characterLine, "collection's ", " character");

            var temporalLine = lines.FirstOrDefault(l =>
                l.Contains("temporal preferences", StringComparison.Ordinal));
            var temporalPref = ExtractBetween(temporalLine, "Align with ", " temporal");

            var discoveryLine = lines.FirstOrDefault(l =>
                l.Contains("discovery pattern", StringComparison.Ordinal));
            var discoveryTrend = ExtractBetween(discoveryLine, "Consider ", " discovery");

            var hasStyleFilters = lines.Any(l =>
                l.Contains("STYLE FILTERS", StringComparison.Ordinal));

            return new PromptSnapshot
            {
                SectionsRendered = sections.ToArray(),
                ArtistGroupCount = artistNames.Count,
                ArtistGroupNames = artistNames.ToArray(),
                PlannerHeader = plannerLine,
                SamplingPreamble = samplingLine,
                RecommendationType = recType,
                ResponseFormat = formatLine,
                CollectionCharacter = collectionCharacter,
                TemporalPreference = temporalPref,
                DiscoveryTrend = discoveryTrend,
                HasStyleFilters = hasStyleFilters,
                NonEmptyLineCount = lines.Count(l => !string.IsNullOrWhiteSpace(l))
            };
        }

        private static string Normalize(string value)
        {
            return value.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private static string ExtractBetween(string? line, string start, string end)
        {
            if (line == null)
            {
                return "";
            }

            var startIdx = line.IndexOf(start, StringComparison.Ordinal);
            if (startIdx < 0)
            {
                return "";
            }

            startIdx += start.Length;
            var endIdx = line.IndexOf(end, startIdx, StringComparison.Ordinal);
            if (endIdx < 0)
            {
                return "";
            }

            return line.Substring(startIdx, endIdx - startIdx);
        }

        private sealed class PromptSnapshot
        {
            public string[] SectionsRendered { get; init; } = Array.Empty<string>();
            public int ArtistGroupCount { get; init; }
            public string[] ArtistGroupNames { get; init; } = Array.Empty<string>();
            public string PlannerHeader { get; init; } = "";
            public string SamplingPreamble { get; init; } = "";
            public string RecommendationType { get; init; } = "";
            public string ResponseFormat { get; init; } = "";
            public string CollectionCharacter { get; init; } = "";
            public string TemporalPreference { get; init; } = "";
            public string DiscoveryTrend { get; init; } = "";
            public bool HasStyleFilters { get; init; }
            public int NonEmptyLineCount { get; init; }
        }

        // Golden JSON snapshot — if this breaks, review the diff to confirm the change is intentional,
        // then update by copying the actual JSON from the test failure message.
        private const string ExpectedGoldenJson =
            "{\n" +
            "  \"SectionsRendered\": [\n" +
            "    \"COLLECTION OVERVIEW\",\n" +
            "    \"MUSICAL DNA\",\n" +
            "    \"COLLECTION PATTERNS\"\n" +
            "  ],\n" +
            "  \"ArtistGroupCount\": 2,\n" +
            "  \"ArtistGroupNames\": [\n" +
            "    \"Pink Floyd\",\n" +
            "    \"Miles Davis\"\n" +
            "  ],\n" +
            "  \"PlannerHeader\": \"[PLANNER] version=2025-09-30-a cache_hit=false seed=golden-test-seed\",\n" +
            "  \"SamplingPreamble\": \"[STYLE_AWARE] Use balanced sampling with key artists/albums.\",\n" +
            "  \"RecommendationType\": \"albums\",\n" +
            "  \"ResponseFormat\": \"default\",\n" +
            "  \"CollectionCharacter\": \"deep-cuts\",\n" +
            "  \"TemporalPreference\": \"classic/modern\",\n" +
            "  \"DiscoveryTrend\": \"accelerating\",\n" +
            "  \"HasStyleFilters\": false,\n" +
            "  \"NonEmptyLineCount\": 43\n" +
            "}";
    }
}
