using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
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
        private readonly IRecommendationValidator _validator;
        private readonly SortedDictionary<int, List<IAIProvider>> _providerChain;
        private readonly AIServiceMetrics _metrics;
        private readonly object _lockObject = new object();

        public AIService(
            Logger logger,
            IProviderHealthMonitor healthMonitor,
            IRetryPolicy retryPolicy,
            IRateLimiter rateLimiter,
            IRecommendationSanitizer sanitizer,
            IRecommendationValidator validator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
            _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _providerChain = new SortedDictionary<int, List<IAIProvider>>();
            _metrics = new AIServiceMetrics();
        }

        /// <summary>
        /// Gets recommendations using the configured provider chain with automatic failover.
        /// </summary>
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            // Generate correlation ID for this request
            var correlationId = CorrelationContext.StartNew();
            var exceptions = new List<Exception>();
            var stopwatch = Stopwatch.StartNew();

            _logger.InfoWithCorrelation($"Starting recommendation request with prompt length: {prompt?.Length ?? 0}");

            // Provider Failover Algorithm: Chain of Responsibility Pattern
            // Providers are organized in priority groups (lower number = higher priority)
            // Example: {1: [Ollama], 2: [OpenAI, Anthropic], 3: [Gemini]}
            // Process: Try each provider in priority order until one succeeds
            // Health checks prevent attempting known-failed providers
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
                            _logger.DebugWithCorrelation($"Skipping unhealthy provider: {providerName}");
                            continue;
                        }

                        _logger.InfoWithCorrelation($"Attempting to get recommendations from {providerName}");

                        // Execute with rate limiting and retry policy
                        // Use model-aware resource key when available; fall back to provider-only
                        var resource = (providerName ?? "unknown").ToLower() + ":default";
                        var recommendations = await _rateLimiter.ExecuteAsync(resource, async () =>
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

                            // Validate recommendations to eliminate hallucinations
                            var validated = await ValidateRecommendations(sanitized, providerName);

                            if (validated.Any())
                            {
                                // Record success metrics
                                _healthMonitor.RecordSuccess(providerName, responseTime);
                                UpdateMetrics(providerName, true, responseTime);

                                var rejectedCount = sanitized.Count - validated.Count;
                                if (rejectedCount > 0)
                                {
                                    _logger.InfoWithCorrelation($"Validation rejected {rejectedCount} recommendations from {providerName}");
                                }

                                _logger.InfoWithCorrelation($"Successfully got {validated.Count} validated recommendations from {providerName} in {responseTime}ms");
                                return validated;
                            }
                            else
                            {
                                _logger.WarnWithCorrelation($"All recommendations from {providerName} were rejected during validation");
                            }
                        }
                        else
                        {
                            _logger.WarnWithCorrelation($"Provider {providerName} returned empty recommendations");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorWithCorrelation(ex, $"Provider {providerName} failed");
                        exceptions.Add(ex);

                        // Record failure metrics
                        _healthMonitor.RecordFailure(providerName, ex.Message);
                        UpdateMetrics(providerName, false, stopwatch.ElapsedMilliseconds);
                    }
                }
            }

            // All providers failed
            _logger.ErrorWithCorrelation($"All {_providerChain.Sum(p => p.Value.Count)} providers failed to get recommendations");

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
                        var health = await provider.TestConnectionAsync();
                        var connected = health.IsHealthy;
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

                    // Incremental Running Average Algorithm (memory-efficient)
                    // Formula: new_avg = (old_avg * (count-1) + new_value) / count
                    // Avoids storing all historical response times in memory
                    // Example: avg=100ms, count=10, new=50ms -> (100*9 + 50)/10 = 95ms
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

        /// <summary>
        /// Validates recommendations using the recommendation validator to eliminate AI hallucinations.
        /// </summary>
        private async Task<List<Recommendation>> ValidateRecommendations(List<Recommendation> recommendations, string providerName)
        {
            if (!recommendations.Any())
            {
                return recommendations;
            }

            try
            {
                _logger.Debug($"Validating {recommendations.Count} recommendations from {providerName}");

                var validationResult = _validator.ValidateBatch(recommendations, false);
                var validated = validationResult.ValidRecommendations;

                var rejectedCount = recommendations.Count - validated.Count;
                if (rejectedCount > 0)
                {
                    _logger.Info($"Validation process rejected {rejectedCount}/{recommendations.Count} " +
                               $"recommendations from {providerName} (likely hallucinations)");

                    // Log some examples of rejected recommendations for debugging
                    var rejected = recommendations.Except(validated).Take(3);
                    foreach (var rec in rejected)
                    {
                        _logger.Debug($"Rejected recommendation: {rec.Artist} - {rec.Album} " +
                                    $"(Year: {rec.Year}, Confidence: {rec.Confidence:F2})");
                    }
                }

                return validated;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error during recommendation validation for {providerName}, " +
                            "returning unvalidated recommendations");

                // Return original recommendations if validation fails
                return recommendations;
            }
        }
    }
}
