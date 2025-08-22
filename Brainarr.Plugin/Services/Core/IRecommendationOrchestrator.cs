using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Orchestrates the complete recommendation workflow including caching, health monitoring,
    /// rate limiting, and intelligent recommendation generation.
    /// Coordinates between AI providers, library analysis, and result processing.
    /// </summary>
    public interface IRecommendationOrchestrator
    {
        /// <summary>
        /// Generates music recommendations using AI providers with intelligent caching and fallback strategies.
        /// Performs health checks, applies rate limiting, and uses library-aware algorithms to ensure
        /// high-quality recommendations tailored to the user's music collection.
        /// </summary>
        /// <param name="settings">Provider configuration including model selection and discovery preferences</param>
        /// <param name="profile">User's library profile containing genres, artists, and listening patterns</param>
        /// <returns>A list of validated import items ready for Lidarr processing</returns>
        /// <remarks>
        /// The method includes several performance optimizations:
        /// - Cache checks to avoid redundant API calls
        /// - Provider health monitoring to prevent failed requests  
        /// - Rate limiting to respect provider API limits
        /// - Iterative refinement for improved recommendation quality
        /// - Automatic fallback to simpler strategies if advanced methods fail
        /// </remarks>
        Task<List<ImportListItemInfo>> GetRecommendationsAsync(
            BrainarrSettings settings, 
            LibraryProfile profile);
        
        /// <summary>
        /// Initializes the orchestrator with the specified AI provider.
        /// Creates provider instances and validates configuration.
        /// </summary>
        /// <param name="settings">Provider configuration settings</param>
        /// <remarks>
        /// Provider initialization failures are logged but do not throw exceptions
        /// to prevent UI disruption. Check logs for initialization errors.
        /// </remarks>
        void InitializeProvider(BrainarrSettings settings);
    }
}