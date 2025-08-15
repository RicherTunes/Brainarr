using System.Collections.Generic;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface ILibraryAwarePromptBuilder
    {
        string BuildLibraryAwarePrompt(
            LibraryProfile profile,
            IList<Artist> allArtists,
            IList<Album> allAlbums,
            BrainarrSettings settings,
            SamplingStrategy strategy = SamplingStrategy.Balanced);
    }
}