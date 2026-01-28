using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Interface for managing AI provider lifecycle operations.
    /// </summary>
    public interface IProviderLifecycleManager
    {
        /// <summary>
        /// Gets the currently initialized provider instance.
        /// </summary>
        IAIProvider CurrentProvider { get; }

        /// <summary>
        /// Gets the type of the currently initialized provider.
        /// </summary>
        AIProvider? CurrentProviderType { get; }

        /// <summary>
        /// Initializes the provider specified in the settings.
        /// </summary>
        void InitializeProvider(BrainarrSettings settings);

        /// <summary>
        /// Updates the provider configuration.
        /// </summary>
        void UpdateProviderConfiguration(BrainarrSettings settings);

        /// <summary>
        /// Tests the connection to the configured provider.
        /// </summary>
        Task<bool> TestProviderConnectionAsync(BrainarrSettings settings);

        /// <summary>
        /// Checks if the current provider is healthy.
        /// </summary>
        bool IsProviderHealthy();

        /// <summary>
        /// Gets the provider status string.
        /// </summary>
        string GetProviderStatus();

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        string GetProviderName();
    }
}
