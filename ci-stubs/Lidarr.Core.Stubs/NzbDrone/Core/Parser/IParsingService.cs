using System.Collections.Generic;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Parser.Model.Quality;

namespace NzbDrone.Core.Parser
{
    public interface IParsingService
    {
        Artist GetArtist(string title);
        ParsedAlbumInfo ParseAlbumTitle(string title);
        ParsedTrackInfo ParseTrackTitle(string title);
        List<Album> GetAlbums(ParsedAlbumInfo parsedAlbumInfo, Artist artist, SearchCriteriaBase searchCriteria = null);
        Artist GetArtistFromTag(string file);
        ParsedArtistInfo ParseArtistTitle(string title);
    }

    public class ParsedAlbumInfo
    {
        public string AlbumTitle { get; set; }
        public string ArtistName { get; set; }
        public string ArtistTitleInfo { get; set; }
        public AlbumTitleInfo AlbumTitleInfo { get; set; }
        public string CleanTitle { get; set; }
        public string MBId { get; set; }
        public int Year { get; set; }
        public string ReleaseGroup { get; set; }
        public string ReleaseHash { get; set; }
        public string ReleaseTitle { get; set; }
        public string ReleaseVersion { get; set; }
        public bool Discography { get; set; }
        public int DiscNumber { get; set; }
        public int DiscCount { get; set; }
        public Quality Quality { get; set; }
    }

    public class ParsedTrackInfo
    {
        public string Title { get; set; }
        public string CleanTitle { get; set; }
        public string ArtistTitle { get; set; }
        public string AlbumTitle { get; set; }
        public string ArtistMBId { get; set; }
        public string AlbumMBId { get; set; }
        public string TrackMBId { get; set; }
        public int TrackNumbers { get; set; }
        public int DiscNumber { get; set; }
        public int DiscCount { get; set; }
        public int Year { get; set; }
        public string ReleaseGroup { get; set; }
        public string ReleaseHash { get; set; }
        public Quality Quality { get; set; }
    }

    public class AlbumTitleInfo
    {
        public string Title { get; set; }
        public string TitleWithoutYear { get; set; }
        public int Year { get; set; }
    }

    public class SearchCriteriaBase
    {
        public override string ToString() => string.Empty;
    }
}

namespace NzbDrone.Core.Parser.Model.Quality
{
    public class Quality
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public QualitySource Source { get; set; }
        public int Resolution { get; set; }

        public static Quality Unknown = new Quality { Id = 0, Name = "Unknown" };
        public static Quality MP3_192 = new Quality { Id = 1, Name = "MP3-192" };
        public static Quality MP3_VBR = new Quality { Id = 2, Name = "MP3-VBR" };
        public static Quality MP3_256 = new Quality { Id = 3, Name = "MP3-256" };
        public static Quality MP3_320 = new Quality { Id = 4, Name = "MP3-320" };
        public static Quality LOSSLESS = new Quality { Id = 5, Name = "Lossless" };
        public static Quality FLAC = new Quality { Id = 6, Name = "FLAC" };

        public override string ToString()
        {
            return Name;
        }
    }

    public enum QualitySource
    {
        Unknown = 0,
        Television = 1,
        TelevisionRaw = 2,
        Web = 3,
        WebRip = 4,
        DVD = 5,
        Bluray = 6,
        BlurayRaw = 7
    }
}