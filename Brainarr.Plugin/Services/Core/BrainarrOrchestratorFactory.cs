using System;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Default factory implementation for creating BrainarrOrchestrator instances.
    /// Handles the complex initialization of all orchestrator dependencies while
    /// maintaining the singleton pattern for shared resources like prompt builders.
    /// </summary>
    public class BrainarrOrchestratorFactory : IBrainarrOrchestratorFactory
    {
        // Singleton instance for prompt builder to avoid multiple instantiations
        // This maintains the original singleton behavior while enabling DI
        private static ILibraryAwarePromptBuilder? _sharedPromptBuilder;
        private static readonly object _promptBuilderLock = new object();

        /// <summary>
        /// Creates a new BrainarrOrchestrator with all necessary dependencies initialized.
        /// Uses singleton pattern for shared resources to maintain performance and consistency.
        /// </summary>
        public IBrainarrOrchestrator Create(
            IHttpClient httpClient,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger)
        {
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (artistService == null) throw new ArgumentNullException(nameof(artistService));
            if (albumService == null) throw new ArgumentNullException(nameof(albumService));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            // Initialize shared prompt builder using thread-safe singleton pattern
            if (_sharedPromptBuilder == null)
            {
                lock (_promptBuilderLock)
                {
                    if (_sharedPromptBuilder == null)
                    {
                        _sharedPromptBuilder = new LibraryAwarePromptBuilder(logger);
                    }
                }
            }

            return new BrainarrOrchestrator(
                httpClient,
                artistService,
                albumService,
                _sharedPromptBuilder,
                logger);
        }
    }
}