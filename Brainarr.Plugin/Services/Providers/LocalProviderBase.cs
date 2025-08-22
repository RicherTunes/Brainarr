using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    public abstract class LocalProviderBase : IAIProvider
    {
        protected readonly IHttpClient _httpClient;
        protected readonly Logger _logger;
        protected readonly string _baseUrl;
        protected string _model;

        public abstract string ProviderName { get; }

        protected LocalProviderBase(IHttpClient httpClient, Logger logger, string baseUrl, string model)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException($"{ProviderName} URL is required", nameof(baseUrl));
            
            _baseUrl = baseUrl.TrimEnd('/');
            _model = model;
            
            _logger.Info($"Initialized {ProviderName} provider at {_baseUrl} with model: {_model}");
        }

        public abstract Task<bool> TestConnectionAsync();
        
        public abstract Task<List<string>> GetAvailableModelsAsync();
        
        public abstract Task<List<Recommendation>> GetRecommendationsAsync(string prompt);

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

        protected bool IsValidModel(string model)
        {
            return !string.IsNullOrWhiteSpace(model) && !model.Equals("local-model", StringComparison.OrdinalIgnoreCase);
        }

        protected virtual int GetMaxTokens()
        {
            return 2000;
        }

        protected virtual double GetTemperature()
        {
            return 0.8;
        }
    }
}