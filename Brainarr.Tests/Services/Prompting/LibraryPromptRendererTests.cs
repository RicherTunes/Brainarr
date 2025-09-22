using System;
using System.Collections.Generic;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;

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
        }
    }
}
