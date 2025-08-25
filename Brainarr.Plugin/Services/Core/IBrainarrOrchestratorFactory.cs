using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Factory interface for creating BrainarrOrchestrator instances.
    /// This enables dependency injection and testing by allowing orchestrator creation
    /// to be controlled externally while maintaining compatibility with Lidarr's DI system.
    /// </summary>
    public interface IBrainarrOrchestratorFactory
    {
        /// <summary>
        /// Creates a new BrainarrOrchestrator instance with the specified dependencies.
        /// </summary>
        /// <param name="httpClient">HTTP client for provider communications</param>
        /// <param name="artistService">Lidarr artist service for library analysis</param>
        /// <param name="albumService">Lidarr album service for library profiling</param>
        /// <param name="logger">Logger instance for monitoring</param>
        /// <returns>A fully configured BrainarrOrchestrator instance</returns>
        IBrainarrOrchestrator Create(
            IHttpClient httpClient,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger);
    }
}