using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    public class TemporalAnalyzer : ITemporalAnalyzer
    {
        private readonly Logger _logger;

        public TemporalAnalyzer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public TemporalAnalysis AnalyzeTemporalPatterns(List<Album> albums)
        {
            var albumsWithDates = albums.Where(a => a.ReleaseDate.HasValue).ToList();

            if (!albumsWithDates.Any())
            {
                return new TemporalAnalysis
                {
                    ReleaseDecades = new List<string>(),
                    PreferredEras = new List<string>(),
                    NewReleaseRatio = 0.0
                };
            }

            // Optimized single-pass analysis
            var decadeGroups = albumsWithDates
                .GroupBy(a => (a.ReleaseDate.Value.Year / 10) * 10)
                .OrderByDescending(g => g.Count())
                .ToList();

            var releaseDecades = ExtractReleaseDecades(decadeGroups);
            var preferredEras = DeterminePreferredEras(decadeGroups);
            var newReleaseRatio = CalculateNewReleaseRatio(albumsWithDates);

            return new TemporalAnalysis
            {
                ReleaseDecades = releaseDecades,
                PreferredEras = preferredEras,
                NewReleaseRatio = newReleaseRatio,
                DecadeDistribution = CalculateDecadeDistribution(decadeGroups, albumsWithDates.Count)
            };
        }

        private List<string> ExtractReleaseDecades(List<IGrouping<int, Album>> decadeGroups)
        {
            return decadeGroups
                .Take(3)
                .Select(g => $"{g.Key}s")
                .ToList();
        }

        private List<string> DeterminePreferredEras(List<IGrouping<int, Album>> decadeGroups)
        {
            var eras = new HashSet<string>();
            
            foreach (var decade in decadeGroups.Take(2))
            {
                var year = decade.Key;
                if (year < 1970) eras.Add("Classic");
                else if (year < 1990) eras.Add("Golden Age");
                else if (year < 2010) eras.Add("Modern");
                else eras.Add("Contemporary");
            }
            
            return eras.ToList();
        }

        private double CalculateNewReleaseRatio(List<Album> albumsWithDates)
        {
            var recentThreshold = DateTime.UtcNow.AddYears(-2);
            var recentAlbums = albumsWithDates.Count(a => a.ReleaseDate.Value > recentThreshold);
            return albumsWithDates.Any() ? (double)recentAlbums / albumsWithDates.Count : 0.0;
        }

        private Dictionary<string, double> CalculateDecadeDistribution(
            List<IGrouping<int, Album>> decadeGroups, 
            int totalAlbums)
        {
            var distribution = new Dictionary<string, double>();
            
            foreach (var group in decadeGroups)
            {
                var decade = $"{group.Key}s";
                var percentage = Math.Round((double)group.Count() / totalAlbums * 100, 1);
                distribution[decade] = percentage;
            }
            
            return distribution;
        }
    }
}