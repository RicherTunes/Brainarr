using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;

public interface ISignatureService
{
    (int Seed, string Fingerprint, string CacheKey) Compose(
        LibraryProfile profile,
        IReadOnlyList<Artist> artists,
        IReadOnlyList<Album> albums,
        StylePlanContext selection,
        BrainarrSettings settings,
        bool recommendArtists,
        string modelKey,
        int contextWindow,
        int targetTokens,
        ICompressionPolicy compressionPolicy);
}
