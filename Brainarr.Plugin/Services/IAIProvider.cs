using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Defines the contract for AI providers that generate music recommendations.
    /// </summary>
    public interface IAIProvider
    {
        /// <summary>
        /// Gets music recommendations based on the provided prompt.
        /// </summary>
        /// <param name="prompt">The prompt describing the user's music library and preferences.</param>
        /// <returns>A list of recommended albums with metadata.</returns>
        Task<List<Recommendation>> GetRecommendationsAsync(string prompt);

        /// <summary>
        /// Gets music recommendations based on the provided prompt with cancellation support.
        /// Default implementation calls the non-cancelable overload.
        /// </summary>
        /// <param name="prompt">Prompt text</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Recommendations</returns>
        Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
            => GetRecommendationsAsync(prompt);

        /// <summary>
        /// Tests the connection to the AI provider.
        /// </summary>
        /// <returns>True if the connection is successful; otherwise, false.</returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// Tests the connection to the AI provider with cancellation.
        /// Default implementation calls the non-cancelable overload.
        /// </summary>
        Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
            => TestConnectionAsync();

        /// <summary>
        /// Gets the display name of the provider.
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Updates the model used by the provider.
        /// </summary>
        /// <param name="modelName">The new model name to use.</param>
        void UpdateModel(string modelName);
    }
}
