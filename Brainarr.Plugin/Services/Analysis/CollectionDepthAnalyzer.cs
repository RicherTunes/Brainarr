using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Analysis
{
    public class CollectionDepthAnalyzer : ICollectionDepthAnalyzer
    {
        private readonly Logger _logger;

        public CollectionDepthAnalyzer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public CollectionDepthAnalysis AnalyzeDepth(List<Artist> artists, List<Album> albums)
        {
            var albumsByArtist = albums.GroupBy(a => a.ArtistId).ToList();
            
            var artistsWithManyAlbums = albumsByArtist.Where(g => g.Count() >= 5).ToList();
            var artistsWithFewAlbums = albumsByArtist.Where(g => g.Count() <= 2).ToList();

            var analysis = new CollectionDepthAnalysis
            {
                CompletionistScore = CalculateCompletionistScore(artistsWithManyAlbums.Count, albumsByArtist.Count),
                CasualCollectorScore = CalculateCasualScore(artistsWithFewAlbums.Count, albumsByArtist.Count),
                PreferredAlbumType = DeterminePreferredAlbumType(albums),
                TopCollectedArtists = GetTopCollectedArtists(albumsByArtist, artists)
            };

            analysis.CollectionStyle = DetermineCollectionStyle(analysis.CompletionistScore, analysis.CasualCollectorScore);
            
            return analysis;
        }

        public CollectionQualityMetrics AnalyzeQuality(List<Artist> artists, List<Album> albums)
        {
            var monitoredArtists = artists.Count(a => a.Monitored);
            var monitoredAlbums = albums.Count(a => a.Monitored);
            
            return new CollectionQualityMetrics
            {
                MonitoredRatio = artists.Any() ? (double)monitoredArtists / artists.Count : 0.0,
                Completeness = albums.Any() ? (double)monitoredAlbums / albums.Count : 0.0,
                AverageAlbumsPerArtist = artists.Any() ? (double)albums.Count / artists.Count : 0.0
            };
        }

        private double CalculateCompletionistScore(int artistsWithManyAlbums, int totalArtistGroups)
        {
            return artistsWithManyAlbums * 100.0 / Math.Max(1, totalArtistGroups);
        }

        private double CalculateCasualScore(int artistsWithFewAlbums, int totalArtistGroups)
        {
            return artistsWithFewAlbums * 100.0 / Math.Max(1, totalArtistGroups);
        }

        private string DetermineCollectionStyle(double completionistScore, double casualScore)
        {
            if (completionistScore > 40)
                return "Completionist - Collects full discographies";
            else if (casualScore > 60)
                return "Casual - Collects select albums";
            else
                return "Balanced - Mix of deep and shallow collections";
        }

        private string DeterminePreferredAlbumType(List<Album> albums)
        {
            var studioAlbums = albums.Count(a => a.AlbumType == "Studio");
            var compilations = albums.Count(a => a.AlbumType == "Compilation" || a.AlbumType == "Greatest Hits");
            return studioAlbums > compilations * 2 ? "Studio Albums" : "Mixed";
        }

        private List<ArtistDepth> GetTopCollectedArtists(List<IGrouping<int, Album>> albumsByArtist, List<Artist> artists)
        {
            return albumsByArtist
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => 
                {
                    var artist = artists.FirstOrDefault(a => a.Id == g.Key);
                    return new ArtistDepth
                    {
                        ArtistId = g.Key,
                        ArtistName = artist?.Name ?? "Unknown",
                        AlbumCount = g.Count(),
                        IsComplete = g.Count() >= 8
                    };
                })
                .ToList();
        }
    }
}