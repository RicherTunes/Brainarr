using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Interface for handling UI actions in the Brainarr plugin.
    /// </summary>
    public interface IBrainarrUIActionHandler
    {
        /// <summary>
        /// Handles a UI action and returns the result.
        /// </summary>
        /// <param name="action">Action name to handle.</param>
        /// <param name="query">Optional query parameters.</param>
        /// <param name="settings">Current settings.</param>
        /// <returns>Action result object.</returns>
        object HandleAction(string action, IDictionary<string, string> query, BrainarrSettings settings);
    }
}
