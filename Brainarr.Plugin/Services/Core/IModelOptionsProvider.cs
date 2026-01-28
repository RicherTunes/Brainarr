using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Interface for providing model options for AI providers.
    /// </summary>
    public interface IModelOptionsProvider
    {
        /// <summary>
        /// Gets model options for the specified provider.
        /// </summary>
        /// <param name="settings">Current settings containing provider and URLs.</param>
        /// <param name="query">Optional query parameters to override provider/baseUrl.</param>
        /// <returns>Object with options array containing { value, name } pairs.</returns>
        Task<object> GetModelOptionsAsync(BrainarrSettings settings, IDictionary<string, string> query = null);

        /// <summary>
        /// Detects available models for the specified provider.
        /// </summary>
        /// <param name="settings">Current settings containing provider and URLs.</param>
        /// <param name="query">Optional query parameters to override provider/baseUrl.</param>
        /// <returns>Object with options array containing detected models.</returns>
        Task<object> DetectModelsAsync(BrainarrSettings settings, IDictionary<string, string> query = null);
    }
}
