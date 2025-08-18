using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Main orchestrator service for AI providers.
    /// Manages provider chains, failover, and health monitoring.
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// Gets recommendations using the configured provider chain.
        /// Automatically falls back to secondary providers on failure.
        /// </summary>
        /// <param name="prompt">The prompt to send to AI providers</param>
        /// <returns>List of recommendations from the first successful provider</returns>
        Task<List<Recommendation>> GetRecommendationsAsync(string prompt);

        /// <summary>
        /// Tests connectivity to all configured providers.
        /// </summary>
        /// <returns>Dictionary of provider names and their connection status</returns>
        Task<Dictionary<string, bool>> TestAllProvidersAsync();

        /// <summary>
        /// Gets the health status of all providers.
        /// </summary>
        /// <returns>Dictionary of provider names and their health status</returns>
        Task<Dictionary<string, ProviderHealthInfo>> GetProviderHealthAsync();

        /// <summary>
        /// Registers a new provider with the service.
        /// </summary>
        /// <param name="provider">Provider to register</param>
        /// <param name="priority">Priority in the failover chain (lower = higher priority)</param>
        void RegisterProvider(IAIProvider provider, int priority = 100);

        /// <summary>
        /// Updates the configuration for all providers.
        /// </summary>
        /// <param name="settings">Updated settings</param>
        void UpdateConfiguration(BrainarrSettings settings);

        /// <summary>
        /// Gets metrics for all provider usage.
        /// </summary>
        /// <returns>Provider usage metrics</returns>
        AIServiceMetrics GetMetrics();
    }

    /// <summary>
    /// Health information for a provider.
    /// </summary>
    public class ProviderHealthInfo
    {
        public string ProviderName { get; set; }
        public HealthStatus Status { get; set; }
        public double SuccessRate { get; set; }
        public double AverageResponseTime { get; set; }
        public int TotalRequests { get; set; }
        public int FailedRequests { get; set; }
        public string LastError { get; set; }
        public bool IsAvailable { get; set; }
    }

    /// <summary>
    /// Metrics for provider usage.
    /// </summary>
    public class AIServiceMetrics
    {
        public Dictionary<string, int> RequestCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, double> AverageResponseTimes { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, int> ErrorCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, long> TotalTokensUsed { get; set; } = new Dictionary<string, long>();
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
    }
}