using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    public class LibraryMetadataAnalyzer : ILibraryMetadataAnalyzer
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, Dictionary<string, int>> _genreCache;
        private static readonly string[] CommonGenres = 
        { 
            "rock", "pop", "jazz", "electronic", "hip hop", "r&b", "soul", "funk",
            "metal", "punk", "indie", "alternative", "country", "folk", "blues",
            "classical", "reggae", "dance", "house", "techno", "ambient", "experimental"
        };

        public LibraryMetadataAnalyzer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _genreCache = new ConcurrentDictionary<string, Dictionary<string, int>>();
        }

        public async Task<GenreAnalysis> AnalyzeGenresAsync(List<Artist> artists, List<Album> albums)
        {
            var genres = new ConcurrentBag<string>();
            
            // Parallel extraction from artists
            await Task.Run(() =>
            {
                Parallel.ForEach(artists.Where(a => a.Metadata?.Value != null), artist =>
                {
                    if (artist.Metadata.Value.Genres?.Any() == true)
                    {
                        foreach (var genre in artist.Metadata.Value.Genres)
                        {
                            genres.Add(genre);
                        }
                    }
                });
            });

            // Parallel extraction from albums
            await Task.Run(() =>
            {
                Parallel.ForEach(albums.Where(a => a.Genres?.Any() == true), album =>
                {
                    foreach (var genre in album.Genres)
                    {
                        genres.Add(genre);
                    }
                });
            });

            // Fallback to overview extraction if no genres found
            if (!genres.Any())
            {
                _logger.Debug("No direct genre data found, using intelligent fallback");
                var overviewGenres = await ExtractGenresFromOverviewsAsync(artists, albums);
                foreach (var genre in overviewGenres)
                {
                    genres.Add(genre);
                }
            }

            // Process and return analysis
            var genreCounts = ProcessGenreCounts(genres);
            return new GenreAnalysis
            {
                GenreCounts = genreCounts,
                Distribution = CalculateGenreDistribution(genreCounts),
                DiversityScore = CalculateGenreDiversity(genreCounts)
            };
        }

        private async Task<List<string>> ExtractGenresFromOverviewsAsync(List<Artist> artists, List<Album> albums)
        {
            var extractedGenres = new ConcurrentBag<string>();

            await Task.Run(() =>
            {
                Parallel.ForEach(artists.Where(a => a.Metadata?.Value?.Overview != null), artist =>
                {
                    var overview = artist.Metadata.Value.Overview.ToLower();
                    foreach (var genre in CommonGenres)
                    {
                        if (overview.Contains(genre))
                        {
                            extractedGenres.Add(char.ToUpper(genre[0]) + genre.Substring(1));
                        }
                    }
                });
            });

            return extractedGenres.ToList();
        }

        private Dictionary<string, int> ProcessGenreCounts(ConcurrentBag<string> genres)
        {
            return genres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .GroupBy(g => g.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        public Dictionary<string, double> CalculateGenreDistribution(Dictionary<string, int> genres)
        {
            if (!genres.Any()) return new Dictionary<string, double>();

            var total = genres.Sum(g => g.Value);
            var distribution = new Dictionary<string, double>();

            foreach (var genre in genres.OrderByDescending(g => g.Value))
            {
                var percentage = Math.Round((double)genre.Value / total * 100, 1);
                distribution[genre.Key] = percentage;

                // Add significance level
                var significanceKey = $"{genre.Key}_significance";
                distribution[significanceKey] = percentage >= 30 ? 3.0 :
                                              percentage >= 15 ? 2.0 :
                                              percentage >= 5 ? 1.0 : 0.5;
            }

            distribution["genre_diversity_score"] = CalculateGenreDiversity(genres);
            distribution["dominant_genre_percentage"] = genres.Values.Max() * 100.0 / total;
            distribution["genre_count"] = genres.Count;

            return distribution;
        }

        private double CalculateGenreDiversity(Dictionary<string, int> genres)
        {
            var total = genres.Sum(g => g.Value);
            double entropy = 0;

            foreach (var count in genres.Values)
            {
                if (count > 0)
                {
                    double probability = (double)count / total;
                    entropy -= probability * Math.Log(probability, 2);
                }
            }

            double maxEntropy = Math.Log(genres.Count, 2);
            return maxEntropy > 0 ? Math.Round(entropy / maxEntropy, 2) : 0;
        }
    }
}