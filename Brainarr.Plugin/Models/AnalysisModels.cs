using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Models
{
    public class GenreAnalysis
    {
        public Dictionary<string, int> GenreCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, double> Distribution { get; set; } = new Dictionary<string, double>();
        public double DiversityScore { get; set; }
    }

    public class TemporalAnalysis
    {
        public List<string> ReleaseDecades { get; set; } = new List<string>();
        public List<string> PreferredEras { get; set; } = new List<string>();
        public double NewReleaseRatio { get; set; }
        public Dictionary<string, double> DecadeDistribution { get; set; } = new Dictionary<string, double>();
    }

    public class CollectionDepthAnalysis
    {
        public double CompletionistScore { get; set; }
        public double CasualCollectorScore { get; set; }
        public string CollectionStyle { get; set; }
        public string PreferredAlbumType { get; set; }
        public List<ArtistDepth> TopCollectedArtists { get; set; } = new List<ArtistDepth>();
    }

    public class ArtistDepth
    {
        public int ArtistId { get; set; }
        public string ArtistName { get; set; }
        public int AlbumCount { get; set; }
        public bool IsComplete { get; set; }
    }

    public class CollectionQualityMetrics
    {
        public double MonitoredRatio { get; set; }
        public double Completeness { get; set; }
        public double AverageAlbumsPerArtist { get; set; }
    }

    public class UserPreferences
    {
        public Dictionary<string, int> AlbumTypes { get; set; } = new Dictionary<string, int>();
        public string DiscoveryTrend { get; set; }
        public List<string> SecondaryTypes { get; set; } = new List<string>();
    }
}