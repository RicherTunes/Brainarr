using System.Collections.Generic;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface ILibraryProfileService
    {
        LibraryProfile GenerateLibraryProfile(
            IEnumerable<Artist> artists,
            IEnumerable<Album> albums,
            BrainarrSettings settings);
        
        string GenerateLibraryFingerprint(LibraryProfile profile);
        
        LibraryProfile GetEnhancedLibraryProfile(
            IArtistService artistService,
            IAlbumService albumService,
            BrainarrSettings settings);
    }
}