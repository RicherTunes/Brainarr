using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface ILibraryContextBuilder
    {
        LibraryProfile BuildProfile(IArtistService artistService, IAlbumService albumService);
        string GenerateFingerprint(LibraryProfile profile);
    }
}