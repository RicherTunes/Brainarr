using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Main orchestrator for AI providers with automatic failover and health monitoring.
    /// Implements chain of responsibility pattern for provider failover.
    /// </summary>
    public class AIService : IAIService
    {
        private readonly Logger _logger;
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly IRecommendationSanitizer _sanitizer;
        private readonly SortedDictionary<int, List<IAIProvider>> _providerChain;
        private readonly AIServiceMetrics _metrics;
        private readonly object _lockObject = new object();

        public AIService(
            Logger logger,
            IProviderHealthMonitor healthMonitor,
            IRetryPolicy retryPolicy,
            IRateLimiter rateLimiter,
            IRecommendationSanitizer sanitizer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
            _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
            _providerChain = new SortedDictionary<int, List<IAIProvider>>();
            _metrics = new AIServiceMetrics();
        }

        /// <summary>
        /// Gets recommendations using the configured provider chain with automatic failover.
        /// </summary>
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            var exceptions = new List<Exception>();
            var stopwatch = Stopwatch.StartNew();

            // Iterate through providers in priority order
            foreach (var priorityGroup in _providerChain)
            {
                foreach (var provider in priorityGroup.Value)
                {
                    var providerName = provider.ProviderName;
                    
                    try
                    {
                        // Check provider health before attempting
                        var health = await _healthMonitor.CheckHealthAsync(providerName, "");
                        if (health == HealthStatus.Unhealthy)
                        {
                            _logger.Debug($"Skipping unhealthy provider: {providerName}");
                            continue;
                        }

                        _logger.Info($"Attempting to get recommendations from {providerName}");
                        
                        // Execute with rate limiting and retry policy
                        var recommendations = await _rateLimiter.ExecuteAsync(providerName.ToLower(), async () =>
                        {
                            return await _retryPolicy.ExecuteAsync(
                                async () => await provider.GetRecommendationsAsync(prompt),
                                $"GetRecommendations_{providerName}");
                        });

                        stopwatch.Stop();
                        var responseTime = stopwatch.ElapsedMilliseconds;

                        if (recommendations != null && recommendations.Any())
                        {
                            // Sanitize recommendations for security
                            var sanitized = _sanitizer.SanitizeRecommendations(recommendations);
                            
                            // Record success metrics
                            _healthMonitor.RecordSuccess(providerName, responseTime);
                            UpdateMetrics(providerName, true, responseTime);
                            
                            _logger.Info($"Successfully got {sanitized.Count} recommendations from {providerName} in {responseTime}ms");
                            return sanitized;
                        }
                        else
                        {
                            _logger.Warn($"Provider {providerName} returned empty recommendations");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Provider {providerName} failed");
                        exceptions.Add(ex);
                        
                        // Record failure metrics
                        _healthMonitor.RecordFailure(providerName, ex.Message);
                        UpdateMetrics(providerName, false, stopwatch.ElapsedMilliseconds);
                    }
                }
            }

            // All providers failed
            _logger.Error($"All {_providerChain.Sum(p => p.Value.Count)} providers failed to get recommendations");
            
            if (exceptions.Any())
            {
                throw new AggregateException("All AI providers failed", exceptions);
            }
            
            return new List<Recommendation>();
        }

        /// <summary>
        /// Tests connectivity to all configured providers.
        /// </summary>
        public async Task<Dictionary<string, bool>> TestAllProvidersAsync()
        {
            var results = new Dictionary<string, bool>();
            
            var tasks = _providerChain
                .SelectMany(p => p.Value)
                .Select(async provider =>
                {
                    try
                    {
                        var connected = await provider.TestConnectionAsync();
                        lock (_lockObject)
                        {
                            results[provider.ProviderName] = connected;
                        }
                        return (provider.ProviderName, connected);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to test connection for {provider.ProviderName}");
                        lock (_lockObject)
                        {
                            results[provider.ProviderName] = false;
                        }
                        return (provider.ProviderName, false);
                    }
                });

            await Task.WhenAll(tasks);
            return results;
        }

        /// <summary>
        /// Gets the health status of all providers.
        /// </summary>
        public async Task<Dictionary<string, ProviderHealthInfo>> GetProviderHealthAsync()
        {
            var healthInfo = new Dictionary<string, ProviderHealthInfo>();
            
            foreach (var priorityGroup in _providerChain)
            {
                foreach (var provider in priorityGroup.Value)
                {
                    var providerName = provider.ProviderName;
                    var status = await _healthMonitor.CheckHealthAsync(providerName, "");
                    
                    // Get metrics for this provider
                    double avgResponseTime = 0;
                    int requestCount = 0;
                    int errorCount = 0;
                    
                    lock (_lockObject)
                    {
                        if (_metrics.RequestCounts.TryGetValue(providerName, out requestCount))
                        {
                            _metrics.AverageResponseTimes.TryGetValue(providerName, out avgResponseTime);
                            _metrics.ErrorCounts.TryGetValue(providerName, out errorCount);
                        }
                    }
                    
                    var successRate = requestCount > 0 ? 
                        (double)(requestCount - errorCount) / requestCount * 100 : 0;
                    
                    healthInfo[providerName] = new ProviderHealthInfo
                    {
                        ProviderName = providerName,
                        Status = status,
                        SuccessRate = successRate,
                        AverageResponseTime = avgResponseTime,
                        TotalRequests = requestCount,
                        FailedRequests = errorCount,
                        IsAvailable = status != HealthStatus.Unhealthy
                    };
                }
            }
            
            return healthInfo;
        }

        /// <summary>
        /// Registers a new provider with the service.
        /// </summary>
        public void RegisterProvider(IAIProvider provider, int priority = 100)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            lock (_lockObject)
            {
                if (!_providerChain.ContainsKey(priority))
                {
                    _providerChain[priority] = new List<IAIProvider>();
                }
                
                _providerChain[priority].Add(provider);
                _logger.Info($"Registered provider {provider.ProviderName} with priority {priority}");
            }
        }

        /// <summary>
        /// Updates the configuration for all providers.
        /// </summary>
        public void UpdateConfiguration(BrainarrSettings settings)
        {
            // This would reinitialize providers based on new settings
            // Implementation depends on how providers handle configuration changes
            _logger.Info("Configuration updated for AI service");
        }

        /// <summary>
        /// Gets metrics for all provider usage.
        /// </summary>
        public AIServiceMetrics GetMetrics()
        {
            lock (_lockObject)
            {
                // Return a copy to prevent external modification
                return new AIServiceMetrics
                {
                    RequestCounts = new Dictionary<string, int>(_metrics.RequestCounts),
                    AverageResponseTimes = new Dictionary<string, double>(_metrics.AverageResponseTimes),
                    ErrorCounts = new Dictionary<string, int>(_metrics.ErrorCounts),
                    TotalTokensUsed = new Dictionary<string, long>(_metrics.TotalTokensUsed),
                    TotalRequests = _metrics.TotalRequests,
                    SuccessfulRequests = _metrics.SuccessfulRequests,
                    FailedRequests = _metrics.FailedRequests
                };
            }
        }

        private void UpdateMetrics(string providerName, bool success, double responseTime)
        {
            lock (_lockObject)
            {
                // Update request counts
                if (!_metrics.RequestCounts.ContainsKey(providerName))
                {
                    _metrics.RequestCounts[providerName] = 0;
                    _metrics.AverageResponseTimes[providerName] = 0;
                    _metrics.ErrorCounts[providerName] = 0;
                }
                
                _metrics.RequestCounts[providerName]++;
                _metrics.TotalRequests++;
                
                if (success)
                {
                    _metrics.SuccessfulRequests++;
                    
                    // Update average response time (running average)
                    var currentAvg = _metrics.AverageResponseTimes[providerName];
                    var count = _metrics.RequestCounts[providerName];
                    _metrics.AverageResponseTimes[providerName] = 
                        (currentAvg * (count - 1) + responseTime) / count;
                }
                else
                {
                    _metrics.FailedRequests++;
                    _metrics.ErrorCounts[providerName]++;
                }
            }
        }
    }
}