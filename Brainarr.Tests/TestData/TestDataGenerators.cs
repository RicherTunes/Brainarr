using System;
using System.Collections.Generic;
using System.Linq;
using Bogus;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.TestData
{
    /// <summary>
    /// Centralized test data generators using Bogus library for consistent, realistic test data.
    /// This addresses the tech lead's feedback about improving test data quality and consistency
    /// across the test suite while reducing manual test data creation boilerplate.
    /// </summary>
    public static class TestDataGenerators
    {
        private static readonly string[] RealGenres = new[]
        {
            "Rock", "Pop", "Hip-Hop", "Electronic", "Jazz", "Classical", "Blues", "Folk",
            "Alternative", "Indie", "Metal", "Punk", "R&B", "Soul", "Funk", "Reggae",
            "Country", "Ambient", "Progressive Rock", "Post-Rock", "Experimental"
        };

        private static readonly string[] RealArtistNames = new[]
        {
            "The Beatles", "Led Zeppelin", "Pink Floyd", "Queen", "The Rolling Stones",
            "Bob Dylan", "David Bowie", "Radiohead", "Nirvana", "AC/DC", "U2",
            "The Who", "Metallica", "Prince", "Michael Jackson", "Madonna", "Stevie Wonder",
            "Aretha Franklin", "Johnny Cash", "Elvis Presley", "Miles Davis", "John Coltrane"
        };

        private static readonly string[] RealAlbumNames = new[]
        {
            "Abbey Road", "Led Zeppelin IV", "The Dark Side of the Moon", "A Night at the Opera",
            "Sticky Fingers", "Highway 61 Revisited", "The Rise and Fall of Ziggy Stardust",
            "OK Computer", "Nevermind", "Back in Black", "The Joshua Tree", "Who's Next",
            "Master of Puppets", "Purple Rain", "Thriller", "Like a Virgin", "Songs in the Key of Life"
        };

        /// <summary>
        /// Generates realistic BrainarrSettings with valid provider configurations.
        /// </summary>
        public static Faker<BrainarrSettings> BrainarrSettingsGenerator => new Faker<BrainarrSettings>()
            .RuleFor(s => s.Provider, f => f.PickRandom<AIProvider>())
            .RuleFor(s => s.MaxRecommendations, f => f.Random.Int(5, 20))
            .RuleFor(s => s.DiscoveryMode, f => f.PickRandom<DiscoveryMode>())
            .RuleFor(s => s.SamplingStrategy, f => f.PickRandom<SamplingStrategy>())
            .RuleFor(s => s.CacheDuration, f => TimeSpan.FromMinutes(f.Random.Int(30, 120)))
            // Provider-specific configurations
            .RuleFor(s => s.OllamaUrl, f => "http://localhost:11434")
            .RuleFor(s => s.OllamaModel, f => f.PickRandom("llama3", "llama3:8b", "mixtral", "codellama"))
            .RuleFor(s => s.LMStudioUrl, f => "http://localhost:1234")
            .RuleFor(s => s.OpenAIApiKey, f => $"sk-{f.Random.AlphaNumeric(48)}")
            .RuleFor(s => s.OpenAIModel, f => f.PickRandom("gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo"))
            .RuleFor(s => s.AnthropicApiKey, f => $"sk-ant-{f.Random.AlphaNumeric(48)}")
            .RuleFor(s => s.AnthropicModel, f => f.PickRandom("claude-3.5-sonnet", "claude-3.5-haiku", "claude-3-opus"))
            .RuleFor(s => s.PerplexityApiKey, f => $"pplx-{f.Random.AlphaNumeric(32)}")
            .RuleFor(s => s.PerplexityModel, f => f.PickRandom("sonar-large", "sonar-small", "sonar-huge"))
            .RuleFor(s => s.OpenRouterApiKey, f => $"sk-or-{f.Random.AlphaNumeric(48)}")
            .RuleFor(s => s.GeminiApiKey, f => f.Random.AlphaNumeric(39))
            .RuleFor(s => s.GroqApiKey, f => $"gsk_{f.Random.AlphaNumeric(48)}")
            .RuleFor(s => s.DeepSeekApiKey, f => f.Random.AlphaNumeric(32));

        /// <summary>
        /// Generates realistic music recommendations with proper artist/album relationships.
        /// </summary>
        public static Faker<Recommendation> RecommendationGenerator => new Faker<Recommendation>()
            .RuleFor(r => r.Artist, f => f.PickRandom(RealArtistNames))
            .RuleFor(r => r.Album, f => f.PickRandom(RealAlbumNames))
            .RuleFor(r => r.Genre, f => f.PickRandom(RealGenres))
            .RuleFor(r => r.Year, f => f.Random.Int(1950, DateTime.Now.Year))
            .RuleFor(r => r.Confidence, f => f.Random.Double(0.6, 1.0))
            .RuleFor(r => r.Reason, f => f.PickRandom(
                "Similar style to your library",
                "Highly rated by users with similar taste",
                "Classic album from your preferred genre",
                "Recommended based on your recent additions",
                "Popular album you might have missed"));

        /// <summary>
        /// Generates realistic library profiles with proper genre distribution.
        /// </summary>
        public static Faker<LibraryProfile> LibraryProfileGenerator => new Faker<LibraryProfile>()
            .RuleFor(p => p.TotalArtists, f => f.Random.Int(10, 1000))
            .RuleFor(p => p.TotalAlbums, f => f.Random.Int(20, 5000))
            .RuleFor(p => p.TopGenres, f => GenerateGenreDistribution(f))
            .RuleFor(p => p.TopArtists, f => f.Random.ListItems(RealArtistNames.ToList(), f.Random.Int(5, 15)))
            .RuleFor(p => p.RecentlyAdded, f => f.Random.ListItems(RealArtistNames.ToList(), f.Random.Int(2, 8)));

        /// <summary>
        /// Generates realistic Artist entities for Lidarr integration tests.
        /// </summary>
        public static Faker<Artist> ArtistGenerator => new Faker<Artist>()
            .RuleFor(a => a.Id, f => f.Random.Int(1, 10000))
            .RuleFor(a => a.Name, f => f.PickRandom(RealArtistNames))
            .RuleFor(a => a.CleanName, (f, a) => a.Name.Replace(" ", "").ToLower())
            .RuleFor(a => a.SortName, (f, a) => a.Name)
            .RuleFor(a => a.Path, (f, a) => $"/music/{a.Name.Replace(" ", "_")}")
            .RuleFor(a => a.Monitored, f => f.Random.Bool(0.8f))
            .RuleFor(a => a.Added, f => f.Date.Between(DateTime.Now.AddYears(-5), DateTime.Now))
            .RuleFor(a => a.QualityProfileId, f => f.Random.Int(1, 5))
            .RuleFor(a => a.MetadataProfileId, f => f.Random.Int(1, 3));

        /// <summary>
        /// Generates realistic Album entities for Lidarr integration tests.
        /// </summary>
        public static Faker<Album> AlbumGenerator => new Faker<Album>()
            .RuleFor(a => a.Id, f => f.Random.Int(1, 50000))
            .RuleFor(a => a.Title, f => f.PickRandom(RealAlbumNames))
            .RuleFor(a => a.CleanTitle, (f, a) => a.Title.Replace(" ", "").ToLower())
            .RuleFor(a => a.ArtistId, f => f.Random.Int(1, 1000))
            .RuleFor(a => a.ReleaseDate, f => f.Date.Between(new DateTime(1950, 1, 1), DateTime.Now))
            .RuleFor(a => a.Monitored, f => f.Random.Bool(0.9f))
            .RuleFor(a => a.Added, f => f.Date.Between(DateTime.Now.AddYears(-2), DateTime.Now))
            .RuleFor(a => a.AlbumType, f => f.PickRandom("Studio", "Live", "Compilation", "EP", "Single"));

        /// <summary>
        /// Generates realistic ImportListItemInfo for testing import list functionality.
        /// </summary>
        public static Faker<ImportListItemInfo> ImportListItemGenerator => new Faker<ImportListItemInfo>()
            .RuleFor(i => i.Artist, f => f.PickRandom(RealArtistNames))
            .RuleFor(i => i.Album, f => f.PickRandom(RealAlbumNames))
            .RuleFor(i => i.ReleaseDate, f => f.Date.Between(new DateTime(1950, 1, 1), DateTime.Now))
            .RuleFor(i => i.ArtistMusicBrainzId, f => f.Random.Guid().ToString())
            .RuleFor(i => i.AlbumMusicBrainzId, f => f.Random.Guid().ToString())
            .RuleFor(i => i.ImportList, f => "Brainarr")
            .RuleFor(i => i.ImportListId, f => f.Random.Int(1, 100));

        /// <summary>
        /// Generates realistic test scenarios for provider configurations.
        /// </summary>
        public static class ProviderScenarios
        {
            public static BrainarrSettings ValidOllamaSettings() => new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama3",
                MaxRecommendations = 10,
                DiscoveryMode = DiscoveryMode.Adjacent
            };

            public static BrainarrSettings ValidOpenAISettings() => new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "sk-test1234567890abcdef1234567890abcdef12345678",
                OpenAIModel = "gpt-4o",
                MaxRecommendations = 15,
                DiscoveryMode = DiscoveryMode.Exploratory
            };

            public static BrainarrSettings InvalidSettings() => new BrainarrSettings
            {
                Provider = (AIProvider)999, // Invalid enum value
                MaxRecommendations = -1     // Invalid recommendation count
            };
        }

        /// <summary>
        /// Common test data sets for consistent testing across scenarios.
        /// </summary>
        public static class CommonTestData
        {
            public static List<Artist> SmallLibraryArtists() => ArtistGenerator.Generate(5);
            public static List<Artist> MediumLibraryArtists() => ArtistGenerator.Generate(50);
            public static List<Artist> LargeLibraryArtists() => ArtistGenerator.Generate(500);

            public static List<Album> SmallLibraryAlbums() => AlbumGenerator.Generate(10);
            public static List<Album> MediumLibraryAlbums() => AlbumGenerator.Generate(150);
            public static List<Album> LargeLibraryAlbums() => AlbumGenerator.Generate(2000);

            public static List<Recommendation> TypicalRecommendations() => RecommendationGenerator.Generate(10);
            public static List<Recommendation> EmptyRecommendations() => new List<Recommendation>();

            public static List<Recommendation> HallucinatedRecommendations() => new List<Recommendation>
            {
                new Recommendation { Artist = "Real Artist", Album = "Album (AI Imagined Version)", Genre = "Rock" },
                new Recommendation { Artist = "Another Artist", Album = "Album (Fan-Curated Edition)", Genre = "Pop" },
                new Recommendation { Artist = "Third Artist", Album = "Album (2030 Anniversary Remaster)", Genre = "Electronic" }
            };
        }

        /// <summary>
        /// Helper method to generate realistic genre distributions.
        /// </summary>
        private static Dictionary<string, int> GenerateGenreDistribution(Faker faker)
        {
            var genreCount = faker.Random.Int(3, 8);
            var selectedGenres = faker.Random.ListItems(RealGenres.ToList(), genreCount);

            var distribution = new Dictionary<string, int>();
            foreach (var genre in selectedGenres)
            {
                distribution[genre] = faker.Random.Int(1, 100);
            }

            return distribution;
        }
    }
}
