using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Interface for provider capability detection.
    /// Allows intelligent provider selection based on capabilities.
    /// </summary>
    public interface IProviderCapabilities
    {
        /// <summary>
        /// Gets the capabilities of a provider.
        /// </summary>
        /// <param name="provider">Provider to check</param>
        /// <returns>Provider capabilities</returns>
        Task<ProviderCapability> GetCapabilitiesAsync(IAIProvider provider);

        /// <summary>
        /// Selects the best provider for a specific task.
        /// </summary>
        /// <param name="providers">Available providers</param>
        /// <param name="requirements">Required capabilities</param>
        /// <returns>Best provider for the task, or null if none suitable</returns>
        IAIProvider SelectBestProvider(IEnumerable<IAIProvider> providers, CapabilityRequirements requirements);
    }

    /// <summary>
    /// Capabilities of an AI provider.
    /// </summary>
    public class ProviderCapability
    {
        public string ProviderName { get; set; }
        public int MaxTokens { get; set; }
        public bool SupportsStreaming { get; set; }
        public bool SupportsJsonMode { get; set; }
        public bool SupportsSystemPrompts { get; set; }
        public bool SupportsFunctionCalling { get; set; }
        public List<string> SupportedLanguages { get; set; } = new List<string>();
        public double EstimatedCostPer1000Tokens { get; set; }
        public int MaxRequestsPerMinute { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public bool IsLocalProvider { get; set; }
        public string ModelVersion { get; set; }
        public long ModelSizeBytes { get; set; }
        public Dictionary<string, object> CustomCapabilities { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Requirements for provider selection.
    /// </summary>
    public class CapabilityRequirements
    {
        public int MinTokens { get; set; }
        public bool RequiresStreaming { get; set; }
        public bool RequiresJsonMode { get; set; }
        public bool RequiresSystemPrompts { get; set; }
        public bool RequiresFunctionCalling { get; set; }
        public bool PreferLocalProvider { get; set; }
        public double MaxCostPer1000Tokens { get; set; }
        public double MaxResponseTimeMs { get; set; }
        public List<string> RequiredLanguages { get; set; } = new List<string>();
    }

    /// <summary>
    /// Provider capability detector implementation.
    /// </summary>
    public class ProviderCapabilityDetector : IProviderCapabilities
    {
        private readonly Dictionary<string, ProviderCapability> _capabilityCache;
        private readonly Logger _logger;

        public ProviderCapabilityDetector(Logger logger)
        {
            _logger = logger;
            _capabilityCache = new Dictionary<string, ProviderCapability>();
            InitializeKnownCapabilities();
        }

        public async Task<ProviderCapability> GetCapabilitiesAsync(IAIProvider provider)
        {
            // Check cache first
            if (_capabilityCache.TryGetValue(provider.ProviderName, out var cached))
            {
                return await Task.FromResult(cached);
            }

            // Detect capabilities dynamically
            var capability = new ProviderCapability
            {
                ProviderName = provider.ProviderName,
                IsLocalProvider = IsLocalProvider(provider.ProviderName),
                SupportsJsonMode = true, // Most modern providers support JSON
                SupportsSystemPrompts = true
            };

            // Provider-specific capability detection
            switch (provider.ProviderName.ToLower())
            {
                case "ollama":
                    capability.MaxTokens = 4096;
                    capability.SupportsStreaming = true;
                    capability.EstimatedCostPer1000Tokens = 0; // Free for local
                    capability.MaxRequestsPerMinute = 1000; // No limit for local
                    break;

                case "lm studio":
                case "lmstudio":
                    capability.MaxTokens = 4096;
                    capability.SupportsStreaming = true;
                    capability.EstimatedCostPer1000Tokens = 0; // Free for local
                    capability.MaxRequestsPerMinute = 1000; // No limit for local
                    break;

                default:
                    // Default capabilities for unknown providers
                    capability.MaxTokens = 2048;
                    capability.MaxRequestsPerMinute = 60;
                    break;
            }

            // Cache the capabilities
            _capabilityCache[provider.ProviderName] = capability;
            
            return await Task.FromResult(capability);
        }

        public IAIProvider SelectBestProvider(IEnumerable<IAIProvider> providers, CapabilityRequirements requirements)
        {
            var scoredProviders = new List<(IAIProvider provider, double score)>();

            foreach (var provider in providers)
            {
                var capability = AsyncHelper.RunSync(() => GetCapabilitiesAsync(provider));
                var score = CalculateProviderScore(capability, requirements);
                
                if (score > 0)
                {
                    scoredProviders.Add((provider, score));
                }
            }

            // Select provider with highest score
            var best = scoredProviders.OrderByDescending(p => p.score).FirstOrDefault();
            
            if (best.provider != null)
            {
                _logger.Info($"Selected provider {best.provider.ProviderName} with score {best.score}");
            }
            
            return best.provider;
        }

        private double CalculateProviderScore(ProviderCapability capability, CapabilityRequirements requirements)
        {
            double score = 100.0;

            // Check hard requirements
            if (requirements.MinTokens > 0 && capability.MaxTokens < requirements.MinTokens)
                return 0;
            
            if (requirements.RequiresStreaming && !capability.SupportsStreaming)
                return 0;
            
            if (requirements.RequiresJsonMode && !capability.SupportsJsonMode)
                return 0;

            // Score based on preferences
            if (requirements.PreferLocalProvider)
            {
                score += capability.IsLocalProvider ? 50 : -20;
            }

            // Cost consideration
            if (requirements.MaxCostPer1000Tokens > 0)
            {
                if (capability.EstimatedCostPer1000Tokens <= requirements.MaxCostPer1000Tokens)
                {
                    score += 20;
                }
                else
                {
                    score -= 30;
                }
            }

            // Response time consideration
            if (requirements.MaxResponseTimeMs > 0 && capability.AverageResponseTimeMs > 0)
            {
                var timeFactor = capability.AverageResponseTimeMs / requirements.MaxResponseTimeMs;
                if (timeFactor <= 1.0)
                {
                    score += 10 * (1.0 - timeFactor);
                }
                else
                {
                    score -= 20 * timeFactor;
                }
            }

            return Math.Max(0, score);
        }

        private bool IsLocalProvider(string providerName)
        {
            var localProviders = new[] { "ollama", "lmstudio", "lm studio", "jan", "jan.ai", "local" };
            return localProviders.Any(p => providerName.ToLower().Contains(p));
        }

        private void InitializeKnownCapabilities()
        {
            // Pre-populate known provider capabilities
            _capabilityCache["Ollama"] = new ProviderCapability
            {
                ProviderName = "Ollama",
                MaxTokens = 4096,
                SupportsStreaming = true,
                SupportsJsonMode = true,
                SupportsSystemPrompts = true,
                IsLocalProvider = true,
                EstimatedCostPer1000Tokens = 0,
                MaxRequestsPerMinute = 1000
            };

            _capabilityCache["LM Studio"] = new ProviderCapability
            {
                ProviderName = "LM Studio",
                MaxTokens = 4096,
                SupportsStreaming = true,
                SupportsJsonMode = true,
                SupportsSystemPrompts = true,
                IsLocalProvider = true,
                EstimatedCostPer1000Tokens = 0,
                MaxRequestsPerMinute = 1000
            };
        }
    }
}