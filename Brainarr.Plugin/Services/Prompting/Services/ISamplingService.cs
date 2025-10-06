using System.Collections.Generic;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;

public interface ISamplingService
{
    LibrarySample Sample(
        IReadOnlyList<Artist> allArtists,
        IReadOnlyList<Album> allAlbums,
        LibraryStyleContext styleContext,
        StylePlanContext selection,
        BrainarrSettings settings,
        int tokenBudget,
        int seed,
        CancellationToken token);
}
