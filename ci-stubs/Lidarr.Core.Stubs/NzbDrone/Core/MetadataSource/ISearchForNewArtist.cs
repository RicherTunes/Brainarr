using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISearchForNewArtist
    {
        Task<List<Artist>> SearchForNewArtist(string title);
        Task<Artist> GetArtistInfo(string foreignArtistId);
        Task<Tuple<string, Artist, List<Album>>> GetArtistInfo(string foreignArtistId, int metadataProfileId);
    }

    public interface ISearchForNewAlbum
    {
        Task<List<Album>> SearchForNewAlbum(string title, string artist);
        Task<Album> GetAlbumInfo(string foreignAlbumId);
        Task<Tuple<string, Album, List<Track>>> GetAlbumInfo(string foreignAlbumId, bool allAlbums);
    }

    public interface IProvideArtistInfo
    {
        Task<Tuple<string, Artist, List<Album>>> GetArtistInfo(string foreignArtistId, int metadataProfileId);
    }

    public interface IProvideAlbumInfo
    {
        Task<Tuple<string, Album, List<Track>>> GetAlbumInfo(string foreignAlbumId, bool allAlbums);
    }
}