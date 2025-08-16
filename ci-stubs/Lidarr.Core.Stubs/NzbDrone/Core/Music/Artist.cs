using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Music
{
    public class Artist : IModelWithId
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string CleanName { get; set; }
        public string SortName { get; set; }
        public string ForeignArtistId { get; set; }
        public string MBId { get; set; }
        public string Disambiguation { get; set; }
        public string Overview { get; set; }
        public ArtistStatusType Status { get; set; }
        public DateTime? Ended { get; set; }
        public List<string> Genres { get; set; }
        public List<ArtistImage> Images { get; set; }
        public List<Link> Links { get; set; }
        public string Path { get; set; }
        public int QualityProfileId { get; set; }
        public int MetadataProfileId { get; set; }
        public bool AlbumFolder { get; set; }
        public bool Monitored { get; set; }
        public DateTime Added { get; set; }
        public AddArtistOptions AddOptions { get; set; }
        public ArtistStatistics Statistics { get; set; }
        public List<Album> Albums { get; set; }
        public int RootFolderPath { get; set; }
        public List<string> Tags { get; set; }

        public Artist()
        {
            Genres = new List<string>();
            Images = new List<ArtistImage>();
            Links = new List<Link>();
            Albums = new List<Album>();
            Tags = new List<string>();
        }
    }

    public class Album : IModelWithId
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string CleanTitle { get; set; }
        public string ForeignAlbumId { get; set; }
        public string MBId { get; set; }
        public int ArtistId { get; set; }
        public Artist Artist { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public List<string> Genres { get; set; }
        public List<string> SecondaryTypes { get; set; }
        public AlbumStatusType AlbumType { get; set; }
        public List<AlbumImage> Images { get; set; }
        public List<Link> Links { get; set; }
        public bool Monitored { get; set; }
        public DateTime Added { get; set; }
        public AddAlbumOptions AddOptions { get; set; }
        public AlbumStatistics Statistics { get; set; }
        public List<Track> Tracks { get; set; }
        public List<string> Tags { get; set; }

        public Album()
        {
            Genres = new List<string>();
            SecondaryTypes = new List<string>();
            Images = new List<AlbumImage>();
            Links = new List<Link>();
            Tracks = new List<Track>();
            Tags = new List<string>();
        }
    }

    public class Track : IModelWithId
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ForeignTrackId { get; set; }
        public string MBId { get; set; }
        public int AlbumId { get; set; }
        public Album Album { get; set; }
        public int TrackNumber { get; set; }
        public int AbsoluteTrackNumber { get; set; }
        public string ExplicitLyrics { get; set; }
        public TrackFile TrackFile { get; set; }
        public int TrackFileId { get; set; }
        public bool Monitored { get; set; }
        public List<string> Tags { get; set; }

        public Track()
        {
            Tags = new List<string>();
        }
    }

    public class TrackFile : IModelWithId
    {
        public int Id { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public DateTime DateAdded { get; set; }
        public string SceneName { get; set; }
        public string ReleaseGroup { get; set; }
        public MediaInfo MediaInfo { get; set; }
        public int AlbumId { get; set; }
        public Album Album { get; set; }
        public List<string> Tags { get; set; }

        public TrackFile()
        {
            Tags = new List<string>();
        }
    }

    public class MediaInfo
    {
        public string AudioFormat { get; set; }
        public int AudioBitrate { get; set; }
        public double AudioChannels { get; set; }
        public int AudioBits { get; set; }
        public int AudioSampleRate { get; set; }
    }

    public enum ArtistStatusType
    {
        Continuing = 0,
        Ended = 1
    }

    public enum AlbumStatusType
    {
        TBA = 0,
        Announced = 1,
        Released = 2
    }

    public class ArtistImage
    {
        public string CoverType { get; set; }
        public string Url { get; set; }
    }

    public class AlbumImage
    {
        public string CoverType { get; set; }
        public string Url { get; set; }
    }

    public class Link
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    public class AddArtistOptions
    {
        public bool SearchForMissingAlbums { get; set; }
        public bool Monitored { get; set; }
    }

    public class AddAlbumOptions
    {
        public bool SearchForNewAlbum { get; set; }
    }

    public class ArtistStatistics
    {
        public int AlbumCount { get; set; }
        public int TrackFileCount { get; set; }
        public int TrackCount { get; set; }
        public int TotalTrackCount { get; set; }
        public long SizeOnDisk { get; set; }
        public double PercentOfTracks => TotalTrackCount == 0 ? 0 : TrackFileCount / (double)TotalTrackCount * 100;
    }

    public class AlbumStatistics
    {
        public int TrackFileCount { get; set; }
        public int TrackCount { get; set; }
        public int TotalTrackCount { get; set; }
        public long SizeOnDisk { get; set; }
        public double PercentOfTracks => TotalTrackCount == 0 ? 0 : TrackFileCount / (double)TotalTrackCount * 100;
    }

    // Services
    public interface IArtistService
    {
        List<Artist> GetAllArtists();
        Artist GetArtist(int artistId);
        List<Artist> GetArtists(IEnumerable<int> artistIds);
        Artist FindById(string foreignArtistId);
        Artist FindByName(string cleanName);
        Artist FindByNameInexact(string cleanName);
        void DeleteArtist(int artistId, bool deleteFiles, bool addExclusion = false);
        List<Artist> GetArtistsWithoutFiles(int pagingSpec);
        Artist AddArtist(Artist newArtist);
        Artist UpdateArtist(Artist artist);
        List<Artist> UpdateArtists(List<Artist> artist, bool useExistingRelativeFolder);
        bool ArtistPathExists(string folder);
        void RemoveAddOptions(Artist artist);
    }

    public interface IAlbumService
    {
        Album GetAlbum(int albumId);
        List<Album> GetAlbums(IEnumerable<int> albumIds);
        List<Album> GetAlbumsByArtist(int artistId);
        List<Album> GetAlbumsByArtistMetadataId(int artistMetadataId);
        Album FindById(string foreignAlbumId);
        Album FindByTitle(int artistMetadataId, string title);
        Album FindByTitleInexact(int artistMetadataId, string title);
        void DeleteAlbum(int albumId, bool deleteFiles, bool addExclusion = false);
        List<Album> GetAlbumsWithoutFiles(int pagingSpec);
        List<Album> AlbumsWithoutFiles(int pagingSpec);
        Album AddAlbum(Album newAlbum);
        Album UpdateAlbum(Album album);
        List<Album> UpdateAlbums(List<Album> albums);
        void RemoveAddOptions(Album album);
        List<Album> GetAllAlbums();
        Album FindAlbumByRelease(string foreignReleaseId);
        List<Album> GetCandidates(int artistMetadataId, string title);
    }
}