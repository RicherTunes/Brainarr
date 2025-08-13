using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Orchestrates the entire recommendation fetching process
    /// </summary>
    public interface IImportListOrchestrator
    {
        /// <summary>
        /// Fetches AI-powered music recommendations
        /// </summary>
        /// <param name="settings">The Brainarr settings</param>
        /// <returns>List of import items for Lidarr</returns>
        Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings);

        /// <summary>
        /// Tests the provider connection and configuration
        /// </summary>
        /// <param name="settings">The settings to test</param>
        /// <param name="failures">Validation failures to populate</param>
        /// <returns>True if test passed, false otherwise</returns>
        Task<bool> TestConfigurationAsync(BrainarrSettings settings, List<ValidationFailure> failures);
    }
}