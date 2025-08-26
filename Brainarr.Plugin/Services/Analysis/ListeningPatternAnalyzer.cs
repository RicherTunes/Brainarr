using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    /// <summary>
    /// Analyzes listening patterns, preferences, and trends in music libraries.
    /// Focuses on temporal patterns, artist preferences, and album characteristics.
    /// </summary>
    public interface IListeningPatternAnalyzer
    {
        ListeningPattern AnalyzePatterns(List<Album> albums, List<Artist> artists);
        TemporalPreferences AnalyzeTemporalPreferences(List<Album> albums);
        ArtistPreferences AnalyzeArtistPreferences(List<Artist> artists);
        AlbumCharacteristics AnalyzeAlbumCharacteristics(List<Album> albums);
    }
    
    public class ListeningPatternAnalyzer : IListeningPatternAnalyzer
    {
        private readonly Logger _logger;
        
        public ListeningPatternAnalyzer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public ListeningPattern AnalyzePatterns(List<Album> albums, List<Artist> artists)
        {
            var pattern = new ListeningPattern();
            
            if (albums != null && albums.Any())
            {
                pattern.TemporalPreferences = AnalyzeTemporalPreferences(albums);
                pattern.AlbumCharacteristics = AnalyzeAlbumCharacteristics(albums);
            }
            
            if (artists != null && artists.Any())
            {
                pattern.ArtistPreferences = AnalyzeArtistPreferences(artists);
            }
            
            // Analyze collection growth
            if (albums != null && albums.Any())
            {
                pattern.CollectionGrowthRate = CalculateCollectionGrowthRate(albums);
                pattern.PreferredAlbumTypes = IdentifyPreferredAlbumTypes(albums);
            }
            
            _logger.Debug($"Analyzed listening patterns for {artists?.Count ?? 0} artists and {albums?.Count ?? 0} albums");
            
            return pattern;
        }
        
        public TemporalPreferences AnalyzeTemporalPreferences(List<Album> albums)
        {
            var preferences = new TemporalPreferences();
            
            if (albums == null || !albums.Any())
            {
                return preferences;
            }
            
            var albumsWithDates = albums.Where(a => a.ReleaseDate.HasValue).ToList();
            
            if (!albumsWithDates.Any())
            {
                return preferences;
            }
            
            // Analyze decades
            var decadeGroups = albumsWithDates
                .GroupBy(a => (a.ReleaseDate.Value.Year / 10) * 10)
                .OrderByDescending(g => g.Count())
                .ToList();
            
            preferences.PreferredDecades = decadeGroups
                .Take(3)
                .Select(g => new DecadePreference
                {
                    Decade = g.Key,
                    Count = g.Count(),
                    Percentage = (double)g.Count() / albumsWithDates.Count
                })
                .ToList();
            
            // Calculate release year statistics
            var releaseYears = albumsWithDates.Select(a => a.ReleaseDate.Value.Year).ToList();
            preferences.AverageReleaseYear = (int)releaseYears.Average();
            preferences.MedianReleaseYear = CalculateMedian(releaseYears);
            preferences.OldestYear = releaseYears.Min();
            preferences.NewestYear = releaseYears.Max();
            
            // Identify if user prefers newer or older music
            var currentYear = DateTime.Now.Year;
            var recentAlbums = albumsWithDates.Count(a => a.ReleaseDate.Value.Year >= currentYear - 5);
            var classicAlbums = albumsWithDates.Count(a => a.ReleaseDate.Value.Year < currentYear - 20);
            
            preferences.PreferenceType = (recentAlbums > classicAlbums) ? "Contemporary" : 
                                        (classicAlbums > recentAlbums) ? "Classic" : "Mixed";
            
            return preferences;
        }
        
        public ArtistPreferences AnalyzeArtistPreferences(List<Artist> artists)
        {
            var preferences = new ArtistPreferences();
            
            if (artists == null || !artists.Any())
            {
                return preferences;
            }
            
            // Artist statistics
            preferences.TotalArtists = artists.Count;
            
            // Artist popularity distribution
            var withRatings = artists.Where(a => a.Ratings?.Value > 0).ToList();
            if (withRatings.Any())
            {
                var ratings = withRatings.Select(a => a.Ratings.Value).ToList();
                preferences.AverageArtistRating = ratings.Average();
                preferences.PreferMainstream = ratings.Average() > 0.7;
            }
            
            // Artist origin analysis
            preferences.CountryDistribution = artists
                .Where(a => !string.IsNullOrWhiteSpace(a.Disambiguation))
                .GroupBy(a => ExtractCountryFromDisambiguation(a.Disambiguation))
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToDictionary(g => g.Key, g => g.Count());
            
            // Active vs disbanded
            var activeCount = artists.Count(a => a.Ended == false);
            var endedCount = artists.Count(a => a.Ended == true);
            preferences.PreferActive = activeCount > endedCount;
            
            return preferences;
        }
        
        public AlbumCharacteristics AnalyzeAlbumCharacteristics(List<Album> albums)
        {
            var characteristics = new AlbumCharacteristics();
            
            if (albums == null || !albums.Any())
            {
                return characteristics;
            }
            
            // Album type distribution
            characteristics.AlbumTypes = albums
                .GroupBy(a => a.AlbumType ?? "Unknown")
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());
            
            // Track count analysis
            var withTracks = albums.Where(a => a.Statistics?.TrackCount > 0).ToList();
            if (withTracks.Any())
            {
                var trackCounts = withTracks.Select(a => a.Statistics.TrackCount).ToList();
                characteristics.AverageTrackCount = trackCounts.Average();
                characteristics.PreferEPs = trackCounts.Average() < 8;
                characteristics.PreferLongAlbums = trackCounts.Average() > 12;
            }
            
            // Duration analysis
            var withDuration = albums.Where(a => a.Statistics?.DurationSeconds > 0).ToList();
            if (withDuration.Any())
            {
                var durations = withDuration.Select(a => a.Statistics.DurationSeconds / 60.0).ToList(); // Convert to minutes
                characteristics.AverageDurationMinutes = durations.Average();
            }
            
            return characteristics;
        }
        
        private double CalculateCollectionGrowthRate(List<Album> albums)
        {
            // Calculate based on added dates if available
            var withAddedDates = albums.Where(a => a.Added.HasValue).ToList();
            if (withAddedDates.Count < 2)
            {
                return 0.0;
            }
            
            var oldestAdded = withAddedDates.Min(a => a.Added.Value);
            var newestAdded = withAddedDates.Max(a => a.Added.Value);
            var daysDiff = (newestAdded - oldestAdded).TotalDays;
            
            if (daysDiff <= 0)
            {
                return 0.0;
            }
            
            // Albums per month
            return (withAddedDates.Count / daysDiff) * 30;
        }
        
        private Dictionary<string, int> IdentifyPreferredAlbumTypes(List<Album> albums)
        {
            return albums
                .Where(a => !string.IsNullOrWhiteSpace(a.AlbumType))
                .GroupBy(a => a.AlbumType)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToDictionary(g => g.Key, g => g.Count());
        }
        
        private int CalculateMedian(List<int> values)
        {
            if (!values.Any())
                return 0;
            
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            
            if (sorted.Count % 2 == 0)
            {
                return (sorted[mid - 1] + sorted[mid]) / 2;
            }
            
            return sorted[mid];
        }
        
        private string ExtractCountryFromDisambiguation(string disambiguation)
        {
            // Simple extraction - could be enhanced with more sophisticated parsing
            if (string.IsNullOrWhiteSpace(disambiguation))
                return null;
            
            // Common country patterns in disambiguation
            var countries = new[] { "USA", "UK", "Canada", "Australia", "Germany", "France", "Japan", "Sweden" };
            
            foreach (var country in countries)
            {
                if (disambiguation.Contains(country, StringComparison.OrdinalIgnoreCase))
                {
                    return country;
                }
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Represents overall listening patterns
    /// </summary>
    public class ListeningPattern
    {
        public TemporalPreferences TemporalPreferences { get; set; } = new();
        public ArtistPreferences ArtistPreferences { get; set; } = new();
        public AlbumCharacteristics AlbumCharacteristics { get; set; } = new();
        public double CollectionGrowthRate { get; set; }
        public Dictionary<string, int> PreferredAlbumTypes { get; set; } = new();
    }
    
    /// <summary>
    /// Temporal (time-based) preferences
    /// </summary>
    public class TemporalPreferences
    {
        public List<DecadePreference> PreferredDecades { get; set; } = new();
        public int AverageReleaseYear { get; set; }
        public int MedianReleaseYear { get; set; }
        public int OldestYear { get; set; }
        public int NewestYear { get; set; }
        public string PreferenceType { get; set; } // Contemporary, Classic, Mixed
    }
    
    /// <summary>
    /// Artist-related preferences
    /// </summary>
    public class ArtistPreferences
    {
        public int TotalArtists { get; set; }
        public double AverageArtistRating { get; set; }
        public bool PreferMainstream { get; set; }
        public bool PreferActive { get; set; }
        public Dictionary<string, int> CountryDistribution { get; set; } = new();
    }
    
    /// <summary>
    /// Album characteristics preferences
    /// </summary>
    public class AlbumCharacteristics
    {
        public Dictionary<string, int> AlbumTypes { get; set; } = new();
        public double AverageTrackCount { get; set; }
        public double AverageDurationMinutes { get; set; }
        public bool PreferEPs { get; set; }
        public bool PreferLongAlbums { get; set; }
    }
    
    /// <summary>
    /// Decade preference information
    /// </summary>
    public class DecadePreference
    {
        public int Decade { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
    }
}