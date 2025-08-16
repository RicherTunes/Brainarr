using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Music;

namespace Brainarr.Plugin.Services.Library
{
    internal class LibraryProfileService
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;
        
        internal LibraryProfileService(IArtistService artistService, IAlbumService albumService, Logger logger)
        {
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        internal LibraryProfile GetLibraryProfile()
        {
            try
            {
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();
                
                if (!artists.Any())
                {
                    _logger.Debug("No artists in library, returning empty profile");
                    return new LibraryProfile();
                }
                
                return BuildProfile(artists, albums);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to build library profile");
                return new LibraryProfile();
            }
        }
        
        private LibraryProfile BuildProfile(List<Artist> artists, List<Album> albums)
        {
            var profile = new LibraryProfile
            {
                TotalArtists = artists.Count,
                TotalAlbums = albums.Count
            };
            
            // Extract genres efficiently
            var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var artist in artists)
            {
                if (artist.Genres != null)
                {
                    foreach (var genre in artist.Genres)
                    {
                        if (!string.IsNullOrWhiteSpace(genre))
                        {
                            genreCounts[genre] = genreCounts.GetValueOrDefault(genre) + 1;
                        }
                    }
                }
            }
            
            profile.TopGenres = genreCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .Select(kvp => kvp.Key)
                .ToList();
            
            // Sample diverse artists
            profile.SampleArtists = SampleDiverseArtists(artists, 20);
            
            // Calculate era distribution
            profile.EraDistribution = CalculateEraDistribution(albums);
            
            // Identify favorite labels
            profile.FavoriteLabels = ExtractTopLabels(albums, 5);
            
            _logger.Debug($"Built library profile: {profile.TotalArtists} artists, {profile.TotalAlbums} albums, {profile.TopGenres.Count} genres");
            
            return profile;
        }
        
        private List<ArtistInfo> SampleDiverseArtists(List<Artist> artists, int sampleSize)
        {
            if (artists.Count <= sampleSize)
            {
                return artists.Select(ConvertToArtistInfo).ToList();
            }
            
            // Sample artists with diversity in genres
            var genreGroups = artists
                .Where(a => a.Genres?.Any() == true)
                .GroupBy(a => a.Genres.FirstOrDefault())
                .ToList();
            
            var sampled = new List<Artist>();
            var artistsPerGenre = Math.Max(1, sampleSize / Math.Max(1, genreGroups.Count));
            
            foreach (var group in genreGroups.Take(sampleSize))
            {
                sampled.AddRange(group.Take(artistsPerGenre));
                if (sampled.Count >= sampleSize) break;
            }
            
            // Fill remaining slots with random artists
            if (sampled.Count < sampleSize)
            {
                var remaining = artists.Except(sampled).ToList();
                var random = new Random();
                while (sampled.Count < sampleSize && remaining.Any())
                {
                    var index = random.Next(remaining.Count);
                    sampled.Add(remaining[index]);
                    remaining.RemoveAt(index);
                }
            }
            
            return sampled.Select(ConvertToArtistInfo).ToList();
        }
        
        private ArtistInfo ConvertToArtistInfo(Artist artist)
        {
            return new ArtistInfo
            {
                Name = artist.Name,
                Genres = artist.Genres?.ToList() ?? new List<string>(),
                AlbumCount = artist.Statistics?.AlbumCount ?? 0
            };
        }
        
        private Dictionary<string, int> CalculateEraDistribution(List<Album> albums)
        {
            var distribution = new Dictionary<string, int>();
            
            foreach (var album in albums.Where(a => a.ReleaseDate.HasValue))
            {
                var year = album.ReleaseDate.Value.Year;
                var era = GetEra(year);
                distribution[era] = distribution.GetValueOrDefault(era) + 1;
            }
            
            return distribution;
        }
        
        private string GetEra(int year)
        {
            return year switch
            {
                < 1960 => "Pre-1960s",
                < 1970 => "1960s",
                < 1980 => "1970s",
                < 1990 => "1980s",
                < 2000 => "1990s",
                < 2010 => "2000s",
                < 2020 => "2010s",
                _ => "2020s+"
            };
        }
        
        private List<string> ExtractTopLabels(List<Album> albums, int topCount)
        {
            var labelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var album in albums)
            {
                if (!string.IsNullOrWhiteSpace(album.Label))
                {
                    labelCounts[album.Label] = labelCounts.GetValueOrDefault(album.Label) + 1;
                }
            }
            
            return labelCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(topCount)
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }
    
    public class LibraryProfile
    {
        public int TotalArtists { get; set; }
        public int TotalAlbums { get; set; }
        public List<string> TopGenres { get; set; } = new List<string>();
        public List<ArtistInfo> SampleArtists { get; set; } = new List<ArtistInfo>();
        public Dictionary<string, int> EraDistribution { get; set; } = new Dictionary<string, int>();
        public List<string> FavoriteLabels { get; set; } = new List<string>();
    }
    
    public class ArtistInfo
    {
        public string Name { get; set; }
        public List<string> Genres { get; set; }
        public int AlbumCount { get; set; }
    }
}