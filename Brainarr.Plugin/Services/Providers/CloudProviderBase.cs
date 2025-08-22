using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    public abstract class CloudProviderBase : IAIProvider
    {
        protected readonly IHttpClient _httpClient;
        protected readonly Logger _logger;
        protected readonly string _apiKey;
        protected string _model;

        public abstract string ProviderName { get; }
        protected abstract string ApiUrl { get; }
        protected abstract string AuthorizationHeader { get; }

        protected CloudProviderBase(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException($"{ProviderName} API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? GetDefaultModel();
            
            _logger.Info($"Initialized {ProviderName} provider with model: {_model}");
        }

        protected abstract string GetDefaultModel();

        public virtual async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.Debug($"Testing connection to {ProviderName}...");
                
                var testPrompt = "Return a simple JSON array with one music recommendation: [{\"artist\":\"Test\",\"album\":\"Test\"}]";
                var result = await GetRecommendationsAsync(testPrompt);
                
                var success = result != null && result.Count > 0;
                _logger.Info($"{ProviderName} connection test: {(success ? "Success" : "Failed")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to test {ProviderName} connection");
                return false;
            }
        }

        public virtual Task<List<string>> GetAvailableModelsAsync()
        {
            return Task.FromResult(new List<string> { _model });
        }

        public abstract Task<List<Recommendation>> GetRecommendationsAsync(string prompt);

        protected HttpRequestBuilder CreateRequest()
        {
            return new HttpRequestBuilder(ApiUrl)
                .SetHeader("Authorization", AuthorizationHeader)
                .SetHeader("Content-Type", "application/json");
        }

        protected string GetSystemPrompt()
        {
            return "You are a music recommendation expert. Always return recommendations in JSON format " +
                   "with fields: artist, album, genre, confidence (0-1), and reason. " +
                   "Provide diverse, high-quality recommendations based on the user's music taste.";
        }

        protected List<Recommendation> ParseRecommendations(string response, string sourceName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(response))
                {
                    _logger.Warn($"Empty response from {sourceName}");
                    return new List<Recommendation>();
                }

                var parser = new MinimalResponseParser(_logger);
                var recommendations = parser.ParseRecommendations(response, sourceName);
                
                _logger.Info($"Successfully parsed {recommendations.Count} recommendations from {sourceName}");
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to parse recommendations from {sourceName}");
                return new List<Recommendation>();
            }
        }

        protected virtual int GetMaxTokens()
        {
            return 2000;
        }

        protected virtual double GetTemperature()
        {
            return 0.8;
        }

        protected virtual int GetTimeout()
        {
            return 30000;
        }
    }
}