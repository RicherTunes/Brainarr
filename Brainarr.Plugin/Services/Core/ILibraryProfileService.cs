using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface ILibraryProfileService
    {
        LibraryProfile GetLibraryProfile();
        string GenerateLibraryFingerprint(LibraryProfile profile);
        List<string> DetermineListeningTrends(LibraryProfile profile);
        LibraryProfile GetCachedProfile(string cacheKey);
        void CacheProfile(string cacheKey, LibraryProfile profile);
    }
}
