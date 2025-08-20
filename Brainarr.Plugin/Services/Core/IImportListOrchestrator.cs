using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Orchestrates the import list workflow, coordinating between providers, cache, and health monitoring.
    /// </summary>
    public interface IImportListOrchestrator
    {
        /// <summary>
        /// Fetches music recommendations using the configured AI provider.
        /// </summary>
        /// <returns>A list of import items ready for Lidarr processing.</returns>
        Task<IList<ImportListItemInfo>> FetchRecommendationsAsync();

        /// <summary>
        /// Initializes the orchestrator with the specified provider.
        /// </summary>
        /// <param name="provider">The AI provider to use for recommendations.</param>
        void InitializeProvider(IAIProvider provider);

        /// <summary>
        /// Gets the current library profile for recommendation generation.
        /// </summary>
        /// <returns>The library profile containing user's music collection metadata.</returns>
        LibraryProfile GetLibraryProfile();

        /// <summary>
        /// Tests the connection to the configured AI provider.
        /// </summary>
        /// <returns>True if the connection is successful; otherwise, false.</returns>
        Task<bool> TestConnectionAsync();
    }
}