using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    /// <summary>
    /// Extracts library profiles and characteristics from music collections.
    /// Focused solely on profile extraction without analysis logic.
    /// </summary>
    public interface ILibraryProfileExtractor
    {
        LibraryProfile ExtractProfile(List<Artist> artists, List<Album> albums, int sampleSize);
        Dictionary<string, int> ExtractGenreDistribution(List<Artist> artists);
        List<string> ExtractTopArtists(List<Artist> artists, int limit);
    }
    
    public class LibraryProfileExtractor : ILibraryProfileExtractor
    {
        private readonly Logger _logger;
        
        public LibraryProfileExtractor(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public LibraryProfile ExtractProfile(List<Artist> artists, List<Album> albums, int sampleSize)
        {
            if (artists == null || !artists.Any())
            {
                _logger.Warn("No artists provided for profile extraction");
                return new LibraryProfile();
            }
            
            var profile = new LibraryProfile
            {
                TotalArtists = artists.Count,
                TotalAlbums = albums?.Count ?? 0,
                SampleSize = Math.Min(sampleSize, artists.Count)
            };
            
            // Sample artists for analysis
            profile.SampledArtists = SampleArtists(artists, profile.SampleSize);
            
            // Extract basic metrics
            if (albums != null && albums.Any())
            {
                profile.AverageAlbumsPerArtist = (double)albums.Count / artists.Count;
                profile.OldestAlbum = albums.Where(a => a.ReleaseDate.HasValue)
                    .OrderBy(a => a.ReleaseDate)
                    .FirstOrDefault();
                profile.NewestAlbum = albums.Where(a => a.ReleaseDate.HasValue)
                    .OrderByDescending(a => a.ReleaseDate)
                    .FirstOrDefault();
            }
            
            _logger.Debug($"Extracted library profile: {profile.TotalArtists} artists, {profile.TotalAlbums} albums");
            
            return profile;
        }
        
        public Dictionary<string, int> ExtractGenreDistribution(List<Artist> artists)
        {
            if (artists == null || !artists.Any())
            {
                return new Dictionary<string, int>();
            }
            
            var genreMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var artist in artists)
            {
                if (artist.Genres != null)
                {
                    foreach (var genre in artist.Genres)
                    {
                        if (!string.IsNullOrWhiteSpace(genre))
                        {
                            var normalizedGenre = NormalizeGenre(genre);
                            genreMap[normalizedGenre] = genreMap.GetValueOrDefault(normalizedGenre) + 1;
                        }
                    }
                }
            }
            
            _logger.Debug($"Extracted {genreMap.Count} unique genres from {artists.Count} artists");
            
            return genreMap;
        }
        
        public List<string> ExtractTopArtists(List<Artist> artists, int limit)
        {
            if (artists == null || !artists.Any())
            {
                return new List<string>();
            }
            
            return artists
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .OrderByDescending(a => a.Ratings?.Value ?? 0)
                .ThenByDescending(a => a.Statistics?.AlbumCount ?? 0)
                .Take(limit)
                .Select(a => a.Name)
                .ToList();
        }
        
        private List<Artist> SampleArtists(List<Artist> artists, int sampleSize)
        {
            if (sampleSize >= artists.Count)
            {
                return artists.ToList();
            }
            
            // Use deterministic sampling for consistency
            var step = Math.Max(1, artists.Count / sampleSize);
            var sampled = new List<Artist>();
            
            for (int i = 0; i < artists.Count && sampled.Count < sampleSize; i += step)
            {
                sampled.Add(artists[i]);
            }
            
            return sampled;
        }
        
        private string NormalizeGenre(string genre)
        {
            // Basic genre normalization
            return genre.Trim()
                .Replace("_", " ")
                .Replace("-", " ")
                .ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// Represents a library profile with key characteristics
    /// </summary>
    public class LibraryProfile
    {
        public int TotalArtists { get; set; }
        public int TotalAlbums { get; set; }
        public int SampleSize { get; set; }
        public double AverageAlbumsPerArtist { get; set; }
        public List<Artist> SampledArtists { get; set; } = new List<Artist>();
        public Album OldestAlbum { get; set; }
        public Album NewestAlbum { get; set; }
    }
}