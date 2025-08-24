using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Plugin.ImportList.Orchestration
{
    /// <summary>
    /// Manages provider failover and fallback strategies
    /// </summary>
    public class FailoverManager
    {
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly Dictionary<AIProvider, List<AIProvider>> _failoverChains;
        private readonly Logger _logger;

        public FailoverManager(IProviderHealthMonitor healthMonitor, Logger logger)
        {
            _healthMonitor = healthMonitor;
            _logger = logger;
            _failoverChains = InitializeFailoverChains();
        }

        /// <summary>
        /// Attempts to get recommendations with automatic failover
        /// </summary>
        public async Task<List<Recommendation>> GetRecommendationsWithFailoverAsync(
            IAIProvider primaryProvider,
            BrainarrSettings settings,
            LibraryProfile libraryProfile,
            Func<AIProvider, Task<IAIProvider>> providerFactory)
        {
            var attemptedProviders = new HashSet<AIProvider>();
            var currentProvider = settings.Provider;
            
            // Try primary provider first
            attemptedProviders.Add(currentProvider);
            
            try
            {
                return await primaryProvider.GetRecommendationsAsync(libraryProfile, settings);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Primary provider {currentProvider} failed, attempting failover");
                _healthMonitor.RecordFailure(currentProvider.ToString(), ex.Message);
            }

            // Attempt failover chain
            if (_failoverChains.TryGetValue(currentProvider, out var failoverChain))
            {
                foreach (var fallbackProvider in failoverChain)
                {
                    if (attemptedProviders.Contains(fallbackProvider))
                        continue;

                    attemptedProviders.Add(fallbackProvider);
                    
                    // Check if fallback provider is healthy
                    var health = await _healthMonitor.CheckHealthAsync(
                        fallbackProvider.ToString(), 
                        GetProviderUrl(settings, fallbackProvider));
                    
                    if (health == HealthStatus.Unhealthy)
                    {
                        _logger.Info($"Skipping unhealthy failover provider {fallbackProvider}");
                        continue;
                    }

                    try
                    {
                        _logger.Info($"Attempting failover to {fallbackProvider}");
                        
                        // Create temporary settings for failover provider
                        var failoverSettings = CloneSettingsWithProvider(settings, fallbackProvider);
                        var failoverProviderInstance = await providerFactory(fallbackProvider);
                        
                        if (failoverProviderInstance != null)
                        {
                            var results = await failoverProviderInstance.GetRecommendationsAsync(
                                libraryProfile, failoverSettings);
                            
                            if (results?.Any() == true)
                            {
                                _logger.Info($"Successfully failed over to {fallbackProvider}");
                                return results;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"Failover provider {fallbackProvider} also failed");
                        _healthMonitor.RecordFailure(fallbackProvider.ToString(), ex.Message);
                    }
                }
            }

            _logger.Error("All providers in failover chain failed");
            return new List<Recommendation>();
        }

        /// <summary>
        /// Initializes failover chains for each provider
        /// </summary>
        private Dictionary<AIProvider, List<AIProvider>> InitializeFailoverChains()
        {
            return new Dictionary<AIProvider, List<AIProvider>>
            {
                // Local providers failover to other local, then cloud
                [AIProvider.Ollama] = new List<AIProvider> 
                { 
                    AIProvider.LMStudio, 
                    AIProvider.OpenAI, 
                    AIProvider.Anthropic 
                },
                
                [AIProvider.LMStudio] = new List<AIProvider> 
                { 
                    AIProvider.Ollama, 
                    AIProvider.OpenAI, 
                    AIProvider.Anthropic 
                },
                
                // Cloud providers failover to other cloud providers
                [AIProvider.OpenAI] = new List<AIProvider> 
                { 
                    AIProvider.Anthropic, 
                    AIProvider.Gemini, 
                    AIProvider.Groq 
                },
                
                [AIProvider.Anthropic] = new List<AIProvider> 
                { 
                    AIProvider.OpenAI, 
                    AIProvider.Gemini, 
                    AIProvider.Groq 
                },
                
                [AIProvider.Gemini] = new List<AIProvider> 
                { 
                    AIProvider.OpenAI, 
                    AIProvider.Anthropic, 
                    AIProvider.OpenRouter 
                },
                
                [AIProvider.Groq] = new List<AIProvider> 
                { 
                    AIProvider.OpenAI, 
                    AIProvider.Anthropic, 
                    AIProvider.OpenRouter 
                },
                
                [AIProvider.OpenRouter] = new List<AIProvider> 
                { 
                    AIProvider.OpenAI, 
                    AIProvider.Anthropic, 
                    AIProvider.Groq 
                },
                
                [AIProvider.AzureOpenAI] = new List<AIProvider> 
                { 
                    AIProvider.OpenAI, 
                    AIProvider.Anthropic, 
                    AIProvider.Gemini 
                }
            };
        }

        private string GetProviderUrl(BrainarrSettings settings, AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => settings.OllamaUrl,
                AIProvider.LMStudio => settings.LMStudioUrl,
                _ => settings.BaseUrl
            };
        }

        private BrainarrSettings CloneSettingsWithProvider(BrainarrSettings original, AIProvider newProvider)
        {
            // Create a shallow clone and update provider
            var clone = (BrainarrSettings)original.MemberwiseClone();
            clone.Provider = newProvider;
            return clone;
        }
    }
}