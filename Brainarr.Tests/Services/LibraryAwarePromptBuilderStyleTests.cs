using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Unit")]
    public class LibraryAwarePromptBuilderStyleTests
    {
        private static BrainarrSettings MakeSettings(params string[] styles)
        {
            return new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                SamplingStrategy = SamplingStrategy.Comprehensive,
                DiscoveryMode = DiscoveryMode.Similar,
                MaxRecommendations = 5,
                StyleFilters = styles ?? System.Array.Empty<string>()
            };
        }

        private static (List<Artist>, List<Album>) MakeLibrary()
        {
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Yes" },
                new Artist { Id = 2, Name = "Miles Davis" }
            };
            var albums = new List<Album>
            {
                new Album { Id = 1, ArtistId = 1, Title = "Close to the Edge", Genres = new List<string> { "Progressive Rock" } },
                new Album { Id = 2, ArtistId = 2, Title = "Kind of Blue", Genres = new List<string> { "Jazz" } }
            };
            return (artists, albums);
        }

        [Fact]
        public void Prompt_Should_Include_Style_Block_When_Selected()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var tokenBudget = new TokenBudgetService(logger);
            var styleCatalog = new StyleCatalogService(logger, new System.Net.Http.HttpClient());
            var builder = new LibraryAwarePromptBuilder(logger, tokenBudget, styleCatalog);

            var profile = new LibraryProfile { TotalArtists = 2, TotalAlbums = 2, TopGenres = new Dictionary<string, int> { { "rock", 1 }, { "jazz", 1 } } };
            var (artists, albums) = MakeLibrary();
            var settings = MakeSettings("progressive-rock");

            var prompt = builder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            prompt.Should().Contain("STYLE FILTERS:");
            prompt.Should().Contain("Progressive Rock");
            prompt.Should().Contain("Do not recommend outside these styles");
        }

        [Fact]
        public void Prompt_Should_Not_Include_Style_Block_When_None_Selected()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var tokenBudget = new TokenBudgetService(logger);
            var styleCatalog = new StyleCatalogService(logger, new System.Net.Http.HttpClient());
            var builder = new LibraryAwarePromptBuilder(logger, tokenBudget, styleCatalog);

            var profile = new LibraryProfile { TotalArtists = 2, TotalAlbums = 2 };
            var (artists, albums) = MakeLibrary();
            var settings = MakeSettings();

            var prompt = builder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            prompt.Should().NotContain("STYLE FILTERS:");
        }
    }
}
