using System;
using System.Collections.Generic;
using System.Linq;
using Bogus;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Helpers
{
    public static class TestDataGenerator
    {
        private static readonly Random _random = new Random();

        // Music genre lists for realistic test data
        private static readonly string[] Genres =
        {
            "Rock", "Pop", "Jazz", "Classical", "Electronic", "Hip Hop", "R&B", "Country",
            "Folk", "Metal", "Indie", "Alternative", "Punk", "Blues", "Soul", "Funk",
            "Reggae", "Latin", "World", "Ambient", "Experimental", "Post-Rock"
        };

        private static readonly string[] ArtistFirstNames =
        {
            "The", "Black", "Red", "Blue", "White", "Dark", "Light", "Electric",
            "Cosmic", "Crystal", "Golden", "Silver", "Iron", "Steel", "Velvet"
        };

        private static readonly string[] ArtistLastNames =
        {
            "Roses", "Tigers", "Lions", "Eagles", "Wolves", "Phoenix", "Dragons",
            "Knights", "Kings", "Queens", "Shadows", "Lights", "Stars", "Moons"
        };

        private static readonly string[] AlbumPrefixes =
        {
            "Tales of", "Return to", "Journey to", "Songs from", "Memories of",
            "Dreams of", "Visions of", "Chronicles of", "Legends of", "Stories from"
        };

        private static readonly string[] AlbumSuffixes =
        {
            "Paradise", "Eternity", "Tomorrow", "Yesterday", "Forever", "Never",
            "Always", "Sometimes", "Nowhere", "Everywhere", "Heaven", "Earth"
        };

        public static Recommendation GenerateRecommendation()
        {
            var faker = new Faker<Recommendation>()
                .RuleFor(r => r.Artist, f => GenerateArtistName())
                .RuleFor(r => r.Album, f => GenerateAlbumName())
                .RuleFor(r => r.Genre, f => f.PickRandom(Genres))
                .RuleFor(r => r.Confidence, f => Math.Round(f.Random.Double(0.5, 1.0), 2))
                .RuleFor(r => r.Reason, f => GenerateReason(f));

            return faker.Generate();
        }

        public static List<Recommendation> GenerateRecommendations(int count)
        {
            return Enumerable.Range(0, count)
                .Select(_ => GenerateRecommendation())
                .ToList();
        }

        public static ImportListItemInfo GenerateImportListItem()
        {
            var rec = GenerateRecommendation();
            return new ImportListItemInfo
            {
                Artist = rec.Artist,
                Album = rec.Album,
                ReleaseDate = DateTime.UtcNow.AddDays(-_random.Next(0, 365 * 5))
            };
        }

        public static List<ImportListItemInfo> GenerateImportListItems(int count)
        {
            return Enumerable.Range(0, count)
                .Select(_ => GenerateImportListItem())
                .ToList();
        }

        public static BrainarrSettings GenerateSettings(AIProvider provider = AIProvider.Ollama)
        {
            var faker = new Faker<BrainarrSettings>()
                .RuleFor(s => s.Provider, provider)
                .RuleFor(s => s.MaxRecommendations, f => f.Random.Int(5, 30))
                .RuleFor(s => s.DiscoveryMode, f => f.PickRandom<DiscoveryMode>())
                .RuleFor(s => s.AutoDetectModel, f => f.Random.Bool());

            var settings = faker.Generate();

            // Set provider-specific URLs
            if (provider == AIProvider.Ollama)
            {
                settings.OllamaUrl = $"http://localhost:{_random.Next(11000, 12000)}";
                settings.OllamaModel = GenerateModelName("ollama");
            }
            else
            {
                settings.LMStudioUrl = $"http://localhost:{_random.Next(1200, 1300)}";
                settings.LMStudioModel = GenerateModelName("lmstudio");
            }

            return settings;
        }

        public static LibraryProfile GenerateLibraryProfile(int artistCount = 100, int albumCount = 500)
        {
            var topGenres = Genres
                .Take(5)
                .ToDictionary(g => g, g => _random.Next(10, 50));

            var topArtists = Enumerable.Range(0, 10)
                .Select(_ => GenerateArtistName())
                .ToList();

            var recentlyAdded = Enumerable.Range(0, 5)
                .Select(_ => GenerateArtistName())
                .ToList();

            return new LibraryProfile
            {
                TotalArtists = artistCount,
                TotalAlbums = albumCount,
                TopGenres = topGenres,
                TopArtists = topArtists,
                RecentlyAdded = recentlyAdded
            };
        }

        public static string GenerateArtistName()
        {
            if (_random.Next(100) < 30) // 30% chance of single name
            {
                var singleNames = new[] { "Beyoncé", "Prince", "Madonna", "Cher", "Seal", "Björk", "Sting" };
                return singleNames[_random.Next(singleNames.Length)];
            }

            var first = ArtistFirstNames[_random.Next(ArtistFirstNames.Length)];
            var last = ArtistLastNames[_random.Next(ArtistLastNames.Length)];
            return $"{first} {last}";
        }

        public static string GenerateAlbumName()
        {
            if (_random.Next(100) < 40) // 40% chance of simple album name
            {
                var simpleNames = new[] { "Debut", "Untitled", "Self-Titled", "Greatest Hits", "Live", "Acoustic" };
                return simpleNames[_random.Next(simpleNames.Length)];
            }

            var prefix = AlbumPrefixes[_random.Next(AlbumPrefixes.Length)];
            var suffix = AlbumSuffixes[_random.Next(AlbumSuffixes.Length)];
            return $"{prefix} {suffix}";
        }

        public static string GenerateModelName(string provider)
        {
            var models = provider.ToLower() switch
            {
                "ollama" => new[] { "llama2:latest", "mistral:7b", "qwen:14b", "phi:latest", "gemma:2b" },
                "lmstudio" => new[] { "TheBloke/Llama-2-7B-GGUF", "mistralai/Mistral-7B-v0.1", "Qwen/Qwen1.5-14B" },
                _ => new[] { "default-model" }
            };

            return models[_random.Next(models.Length)];
        }

        private static string GenerateReason(Faker faker)
        {
            var reasons = new[]
            {
                "Similar to your favorite artists",
                "Matches your listening patterns",
                "Highly rated by similar listeners",
                "Trending in your preferred genres",
                "Classic album you might have missed",
                "New release from related artist",
                "Critically acclaimed in this genre",
                "Recommended based on your recent additions"
            };

            return faker.PickRandom(reasons);
        }

        public static string GenerateJsonResponse(int recommendationCount)
        {
            var recommendations = GenerateRecommendations(recommendationCount);
            return Newtonsoft.Json.JsonConvert.SerializeObject(recommendations);
        }

        public static string GenerateTextResponse(int recommendationCount)
        {
            var lines = new List<string>();
            for (int i = 0; i < recommendationCount; i++)
            {
                var artist = GenerateArtistName();
                var album = GenerateAlbumName();
                lines.Add($"{i + 1}. {artist} - {album}");
            }
            return string.Join("\n", lines);
        }

        public static string GenerateMalformedJsonResponse()
        {
            var responses = new[]
            {
                "[{\"artist\": \"Test\", \"album\": ", // Truncated
                "{\"data\": [{\"artist\": \"Test\"}]", // Missing closing braces
                "[{artist: 'Test', album: 'Album'}]", // Invalid JSON (no quotes on keys)
                "{'artist': 'Test', 'album': 'Album'}", // Single quotes instead of double
                "[{\"artist\": \"Test\" \"album\": \"Album\"}]", // Missing comma
            };

            return responses[_random.Next(responses.Length)];
        }

        public static Dictionary<string, object> GenerateProviderConfig(string provider)
        {
            var config = new Dictionary<string, object>();

            switch (provider.ToLower())
            {
                case "ollama":
                    config["url"] = $"http://localhost:{_random.Next(11000, 12000)}";
                    config["model"] = GenerateModelName("ollama");
                    config["temperature"] = Math.Round(_random.NextDouble() * 0.5 + 0.5, 2);
                    break;

                case "lmstudio":
                    config["url"] = $"http://localhost:{_random.Next(1200, 1300)}";
                    config["model"] = GenerateModelName("lmstudio");
                    config["max_tokens"] = _random.Next(1000, 3000);
                    break;

                default:
                    config["enabled"] = true;
                    break;
            }

            return config;
        }

        public static List<string> GenerateModelList(int count = 5)
        {
            var models = new HashSet<string>();
            var providers = new[] { "ollama", "lmstudio" };

            while (models.Count < count)
            {
                var provider = providers[_random.Next(providers.Length)];
                models.Add(GenerateModelName(provider));
            }

            return models.ToList();
        }

        // Generate edge case data
        public static class EdgeCases
        {
            public static Recommendation EmptyRecommendation()
            {
                return new Recommendation
                {
                    Artist = "",
                    Album = "",
                    Genre = "",
                    Confidence = 0,
                    Reason = ""
                };
            }

            public static Recommendation NullFieldsRecommendation()
            {
                return new Recommendation
                {
                    Artist = null,
                    Album = null,
                    Genre = null,
                    Confidence = 0,
                    Reason = null
                };
            }

            public static Recommendation UnicodeRecommendation()
            {
                return new Recommendation
                {
                    Artist = "Björk 🎵",
                    Album = "Homogénic 🎸",
                    Genre = "Electronic 🎹",
                    Confidence = 0.9,
                    Reason = "Similar to your Icelandic collection 🇮🇸"
                };
            }

            public static Recommendation VeryLongRecommendation()
            {
                return new Recommendation
                {
                    Artist = new string('A', 499),
                    Album = new string('B', 499),
                    Genre = new string('C', 99),
                    Confidence = 0.5,
                    Reason = new string('D', 999)
                };
            }

            public static Recommendation SpecialCharactersRecommendation()
            {
                return new Recommendation
                {
                    Artist = "AC/DC & Friends",
                    Album = "Live @ Madison Square Garden (Deluxe Edition) [Remastered]",
                    Genre = "Rock & Roll",
                    Confidence = 0.95,
                    Reason = "Based on your love for rock & roll classics"
                };
            }

            public static List<Recommendation> DuplicateRecommendations(int count)
            {
                var rec = GenerateRecommendation();
                return Enumerable.Repeat(rec, count).ToList();
            }
        }
    }
}
