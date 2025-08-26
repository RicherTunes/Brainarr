using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    /// <summary>
    /// Calculates statistical metrics for music libraries.
    /// Provides quantitative analysis without interpretation logic.
    /// </summary>
    public interface ILibraryStatisticsCalculator
    {
        LibraryStatistics CalculateStatistics(List<Artist> artists, List<Album> albums);
        CollectionMetrics CalculateCollectionMetrics(List<Album> albums);
        QualityMetrics CalculateQualityMetrics(List<Artist> artists, List<Album> albums);
        GrowthMetrics CalculateGrowthMetrics(List<Album> albums);
    }
    
    public class LibraryStatisticsCalculator : ILibraryStatisticsCalculator
    {
        private readonly Logger _logger;
        
        public LibraryStatisticsCalculator(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public LibraryStatistics CalculateStatistics(List<Artist> artists, List<Album> albums)
        {
            var stats = new LibraryStatistics
            {
                TotalArtists = artists?.Count ?? 0,
                TotalAlbums = albums?.Count ?? 0
            };
            
            if (artists != null && artists.Any())
            {
                stats.ArtistMetrics = CalculateArtistMetrics(artists);
            }
            
            if (albums != null && albums.Any())
            {
                stats.CollectionMetrics = CalculateCollectionMetrics(albums);
                stats.GrowthMetrics = CalculateGrowthMetrics(albums);
            }
            
            if (artists != null && albums != null)
            {
                stats.QualityMetrics = CalculateQualityMetrics(artists, albums);
            }
            
            // Calculate overall library score
            stats.LibraryScore = CalculateLibraryScore(stats);
            
            _logger.Debug($"Calculated statistics for library: {stats.TotalArtists} artists, {stats.TotalAlbums} albums, score: {stats.LibraryScore:F2}");
            
            return stats;
        }
        
        public CollectionMetrics CalculateCollectionMetrics(List<Album> albums)
        {
            var metrics = new CollectionMetrics();
            
            if (albums == null || !albums.Any())
            {
                return metrics;
            }
            
            // Size metrics
            metrics.TotalAlbums = albums.Count;
            metrics.UniqueArtistCount = albums.Select(a => a.ArtistId).Distinct().Count();
            
            // Track metrics
            var withTracks = albums.Where(a => a.Statistics?.TrackCount > 0).ToList();
            if (withTracks.Any())
            {
                var trackCounts = withTracks.Select(a => a.Statistics.TrackCount).ToList();
                metrics.TotalTracks = trackCounts.Sum();
                metrics.AverageTracksPerAlbum = trackCounts.Average();
                metrics.MedianTracksPerAlbum = CalculateMedian(trackCounts);
            }
            
            // Duration metrics
            var withDuration = albums.Where(a => a.Statistics?.DurationSeconds > 0).ToList();
            if (withDuration.Any())
            {
                var durations = withDuration.Select(a => a.Statistics.DurationSeconds).ToList();
                metrics.TotalDurationHours = durations.Sum() / 3600.0;
                metrics.AverageDurationMinutes = durations.Average() / 60.0;
                metrics.MedianDurationMinutes = CalculateMedian(durations) / 60.0;
            }
            
            // Album type distribution
            metrics.AlbumTypeDistribution = albums
                .GroupBy(a => a.AlbumType ?? "Unknown")
                .ToDictionary(g => g.Key, g => (double)g.Count() / albums.Count);
            
            // Release year spread
            var withDates = albums.Where(a => a.ReleaseDate.HasValue).ToList();
            if (withDates.Any())
            {
                var years = withDates.Select(a => a.ReleaseDate.Value.Year).ToList();
                metrics.OldestReleaseYear = years.Min();
                metrics.NewestReleaseYear = years.Max();
                metrics.ReleaseYearSpan = metrics.NewestReleaseYear - metrics.OldestReleaseYear;
                metrics.ReleaseYearStandardDeviation = CalculateStandardDeviation(years);
            }
            
            return metrics;
        }
        
        public QualityMetrics CalculateQualityMetrics(List<Artist> artists, List<Album> albums)
        {
            var metrics = new QualityMetrics();
            
            // Artist quality metrics
            if (artists != null && artists.Any())
            {
                var withRatings = artists.Where(a => a.Ratings?.Value > 0).ToList();
                if (withRatings.Any())
                {
                    var ratings = withRatings.Select(a => a.Ratings.Value).ToList();
                    metrics.AverageArtistRating = ratings.Average();
                    metrics.MedianArtistRating = CalculateMedian(ratings);
                    metrics.ArtistRatingStandardDeviation = CalculateStandardDeviation(ratings);
                    metrics.HighRatedArtistPercentage = ratings.Count(r => r >= 0.8) / (double)ratings.Count;
                }
                
                // Completeness metrics
                metrics.ArtistsWithGenres = artists.Count(a => a.Genres != null && a.Genres.Any()) / (double)artists.Count;
                metrics.ArtistsWithImages = artists.Count(a => a.Images != null && a.Images.Any()) / (double)artists.Count;
            }
            
            // Album quality metrics
            if (albums != null && albums.Any())
            {
                var withRatings = albums.Where(a => a.Ratings?.Value > 0).ToList();
                if (withRatings.Any())
                {
                    var ratings = withRatings.Select(a => a.Ratings.Value).ToList();
                    metrics.AverageAlbumRating = ratings.Average();
                    metrics.MedianAlbumRating = CalculateMedian(ratings);
                    metrics.AlbumRatingStandardDeviation = CalculateStandardDeviation(ratings);
                }
                
                // Completeness metrics
                metrics.AlbumsWithReleaseDates = albums.Count(a => a.ReleaseDate.HasValue) / (double)albums.Count;
                metrics.AlbumsWithImages = albums.Count(a => a.Images != null && a.Images.Any()) / (double)albums.Count;
                metrics.AlbumsWithGenres = albums.Count(a => a.Genres != null && a.Genres.Any()) / (double)albums.Count;
            }
            
            // Calculate overall quality score (0-100)
            metrics.OverallQualityScore = CalculateQualityScore(metrics);
            
            return metrics;
        }
        
        public GrowthMetrics CalculateGrowthMetrics(List<Album> albums)
        {
            var metrics = new GrowthMetrics();
            
            if (albums == null || !albums.Any())
            {
                return metrics;
            }
            
            var withAddedDates = albums.Where(a => a.Added.HasValue).ToList();
            if (withAddedDates.Count < 2)
            {
                return metrics;
            }
            
            // Sort by added date
            var sorted = withAddedDates.OrderBy(a => a.Added.Value).ToList();
            
            // Time span metrics
            metrics.FirstAddedDate = sorted.First().Added.Value;
            metrics.LastAddedDate = sorted.Last().Added.Value;
            metrics.CollectionAgeInDays = (metrics.LastAddedDate - metrics.FirstAddedDate).TotalDays;
            
            // Growth rate calculations
            if (metrics.CollectionAgeInDays > 0)
            {
                metrics.AlbumsPerDay = sorted.Count / metrics.CollectionAgeInDays;
                metrics.AlbumsPerMonth = metrics.AlbumsPerDay * 30;
                metrics.AlbumsPerYear = metrics.AlbumsPerDay * 365;
            }
            
            // Monthly growth analysis
            var monthlyGroups = sorted.GroupBy(a => new { a.Added.Value.Year, a.Added.Value.Month });
            var monthlyAdditions = monthlyGroups.Select(g => g.Count()).ToList();
            
            if (monthlyAdditions.Any())
            {
                metrics.AverageMonthlyAdditions = monthlyAdditions.Average();
                metrics.PeakMonthlyAdditions = monthlyAdditions.Max();
                metrics.MonthlyGrowthStandardDeviation = CalculateStandardDeviation(monthlyAdditions);
            }
            
            // Recent growth trend (last 3 months vs previous 3 months)
            metrics.GrowthTrend = CalculateGrowthTrend(sorted);
            
            return metrics;
        }
        
        private ArtistMetrics CalculateArtistMetrics(List<Artist> artists)
        {
            var metrics = new ArtistMetrics
            {
                Total = artists.Count,
                Active = artists.Count(a => a.Ended == false),
                Ended = artists.Count(a => a.Ended == true)
            };
            
            // Album count distribution
            var albumCounts = artists
                .Where(a => a.Statistics != null)
                .Select(a => a.Statistics.AlbumCount)
                .ToList();
            
            if (albumCounts.Any())
            {
                metrics.AverageAlbumsPerArtist = albumCounts.Average();
                metrics.MaxAlbumsPerArtist = albumCounts.Max();
                metrics.MedianAlbumsPerArtist = CalculateMedian(albumCounts);
            }
            
            // Genre diversity
            var uniqueGenres = artists
                .Where(a => a.Genres != null)
                .SelectMany(a => a.Genres)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            
            metrics.GenreDiversity = uniqueGenres / (double)artists.Count;
            
            return metrics;
        }
        
        private double CalculateLibraryScore(LibraryStatistics stats)
        {
            double score = 0;
            
            // Size component (0-25 points)
            score += Math.Min(25, stats.TotalAlbums / 20.0); // 500 albums = 25 points
            
            // Diversity component (0-25 points)
            if (stats.ArtistMetrics != null)
            {
                score += Math.Min(25, stats.ArtistMetrics.GenreDiversity * 25);
            }
            
            // Quality component (0-25 points)
            if (stats.QualityMetrics != null)
            {
                score += stats.QualityMetrics.OverallQualityScore * 0.25;
            }
            
            // Growth component (0-25 points)
            if (stats.GrowthMetrics != null && stats.GrowthMetrics.AlbumsPerMonth > 0)
            {
                score += Math.Min(25, stats.GrowthMetrics.AlbumsPerMonth * 5);
            }
            
            return Math.Min(100, score);
        }
        
        private double CalculateQualityScore(QualityMetrics metrics)
        {
            double score = 0;
            int components = 0;
            
            if (metrics.AverageArtistRating > 0)
            {
                score += metrics.AverageArtistRating * 100;
                components++;
            }
            
            if (metrics.AverageAlbumRating > 0)
            {
                score += metrics.AverageAlbumRating * 100;
                components++;
            }
            
            // Data completeness
            score += metrics.ArtistsWithGenres * 20;
            score += metrics.AlbumsWithReleaseDates * 20;
            components += 2;
            
            return components > 0 ? score / components : 0;
        }
        
        private string CalculateGrowthTrend(List<Album> sortedAlbums)
        {
            var cutoffDate = DateTime.Now.AddMonths(-3);
            var recentAdditions = sortedAlbums.Count(a => a.Added.Value >= cutoffDate);
            
            var previousCutoff = cutoffDate.AddMonths(-3);
            var previousAdditions = sortedAlbums.Count(a => a.Added.Value >= previousCutoff && a.Added.Value < cutoffDate);
            
            if (recentAdditions > previousAdditions * 1.2)
                return "Accelerating";
            else if (recentAdditions < previousAdditions * 0.8)
                return "Slowing";
            else
                return "Stable";
        }
        
        private double CalculateMedian<T>(List<T> values) where T : IComparable<T>
        {
            if (!values.Any())
                return 0;
            
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            
            if (sorted.Count % 2 == 0)
            {
                return (Convert.ToDouble(sorted[mid - 1]) + Convert.ToDouble(sorted[mid])) / 2;
            }
            
            return Convert.ToDouble(sorted[mid]);
        }
        
        private double CalculateStandardDeviation<T>(List<T> values) where T : IConvertible
        {
            if (values.Count < 2)
                return 0;
            
            var doubleValues = values.Select(v => Convert.ToDouble(v)).ToList();
            var mean = doubleValues.Average();
            var sumOfSquares = doubleValues.Sum(v => Math.Pow(v - mean, 2));
            
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
    }
    
    // Data models
    public class LibraryStatistics
    {
        public int TotalArtists { get; set; }
        public int TotalAlbums { get; set; }
        public ArtistMetrics ArtistMetrics { get; set; }
        public CollectionMetrics CollectionMetrics { get; set; }
        public QualityMetrics QualityMetrics { get; set; }
        public GrowthMetrics GrowthMetrics { get; set; }
        public double LibraryScore { get; set; }
    }
    
    public class ArtistMetrics
    {
        public int Total { get; set; }
        public int Active { get; set; }
        public int Ended { get; set; }
        public double AverageAlbumsPerArtist { get; set; }
        public int MaxAlbumsPerArtist { get; set; }
        public double MedianAlbumsPerArtist { get; set; }
        public double GenreDiversity { get; set; }
    }
    
    public class CollectionMetrics
    {
        public int TotalAlbums { get; set; }
        public int UniqueArtistCount { get; set; }
        public int TotalTracks { get; set; }
        public double TotalDurationHours { get; set; }
        public double AverageTracksPerAlbum { get; set; }
        public double MedianTracksPerAlbum { get; set; }
        public double AverageDurationMinutes { get; set; }
        public double MedianDurationMinutes { get; set; }
        public Dictionary<string, double> AlbumTypeDistribution { get; set; } = new();
        public int OldestReleaseYear { get; set; }
        public int NewestReleaseYear { get; set; }
        public int ReleaseYearSpan { get; set; }
        public double ReleaseYearStandardDeviation { get; set; }
    }
    
    public class QualityMetrics
    {
        public double AverageArtistRating { get; set; }
        public double MedianArtistRating { get; set; }
        public double ArtistRatingStandardDeviation { get; set; }
        public double HighRatedArtistPercentage { get; set; }
        public double AverageAlbumRating { get; set; }
        public double MedianAlbumRating { get; set; }
        public double AlbumRatingStandardDeviation { get; set; }
        public double ArtistsWithGenres { get; set; }
        public double ArtistsWithImages { get; set; }
        public double AlbumsWithReleaseDates { get; set; }
        public double AlbumsWithImages { get; set; }
        public double AlbumsWithGenres { get; set; }
        public double OverallQualityScore { get; set; }
    }
    
    public class GrowthMetrics
    {
        public DateTime FirstAddedDate { get; set; }
        public DateTime LastAddedDate { get; set; }
        public double CollectionAgeInDays { get; set; }
        public double AlbumsPerDay { get; set; }
        public double AlbumsPerMonth { get; set; }
        public double AlbumsPerYear { get; set; }
        public double AverageMonthlyAdditions { get; set; }
        public int PeakMonthlyAdditions { get; set; }
        public double MonthlyGrowthStandardDeviation { get; set; }
        public string GrowthTrend { get; set; } // Accelerating, Stable, Slowing
    }
}