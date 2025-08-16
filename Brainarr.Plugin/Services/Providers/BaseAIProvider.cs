using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Base class for AI providers to reduce code duplication
    /// </summary>
    public abstract class BaseAIProvider : IAIProvider
    {
        protected readonly IHttpClient _httpClient;
        protected readonly Logger _logger;
        protected readonly string _apiKey;
        protected readonly string _model;

        public abstract string ProviderName { get; }
        protected abstract string ApiUrl { get; }
        protected virtual string SystemPrompt => "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, year (if known), confidence (0-1), and reason.";

        protected BaseAIProvider(IHttpClient httpClient, Logger logger, string apiKey, string model)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey) && RequiresApiKey)
                throw new ArgumentException($"{ProviderName} API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model;

            _logger.Info($"Initialized {ProviderName} provider with model: {_model}");
        }

        protected virtual bool RequiresApiKey => true;

        public virtual async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            try
            {
                var requestBody = BuildRequestBody(prompt);
                var request = BuildHttpRequest(requestBody);
                var response = await ExecuteRequestAsync(request);

                if (!IsSuccessResponse(response))
                {
                    ErrorHandling.HandleHttpError(ProviderName, response.StatusCode, response.Content, _logger);
                    return new List<Recommendation>();
                }

                var content = ExtractContentFromResponse(response.Content);

                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn($"Empty response from {ProviderName}");
                    return new List<Recommendation>();
                }

                return ParseRecommendations(content);
            }
            catch (Exception ex)
            {
                return ErrorHandling.HandleProviderError(ProviderName, ex, _logger);
            }
        }

        public virtual async Task<bool> TestConnectionAsync()
        {
            try
            {
                var requestBody = BuildTestRequestBody();
                var request = BuildHttpRequest(requestBody);
                var response = await ExecuteRequestAsync(request);

                return IsSuccessResponse(response);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Connection test failed for {ProviderName}: {ex.Message}");
                return false;
            }
        }

        public virtual async Task<List<string>> GetAvailableModelsAsync()
        {
            // Default implementation - override in providers that support model listing
            await Task.CompletedTask;
            return new List<string> { _model };
        }

        protected abstract object BuildRequestBody(string prompt);

        protected virtual object BuildTestRequestBody()
        {
            return BuildRequestBody("Reply with 'OK'");
        }

        protected virtual HttpRequest BuildHttpRequest(object requestBody)
        {
            var request = new HttpRequestBuilder(ApiUrl)
                .SetHeader("Content-Type", "application/json")
                .SetHeader("Accept", "application/json")
                .Build();

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            }

            request.Method = HttpMethod.Post;
            request.SetContent(JsonConvert.SerializeObject(requestBody));

            return request;
        }

        protected virtual async Task<HttpResponse> ExecuteRequestAsync(HttpRequest request)
        {
            return await _httpClient.ExecuteAsync(request);
        }

        protected virtual bool IsSuccessResponse(HttpResponse response)
        {
            return response.StatusCode == System.Net.HttpStatusCode.OK;
        }

        protected abstract string ExtractContentFromResponse(string responseContent);

        protected virtual List<Recommendation> ParseRecommendations(string content)
        {
            try
            {
                // Try to extract JSON from the response
                var jsonStart = content.IndexOf('[');
                var jsonEnd = content.LastIndexOf(']');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonContent = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var recommendations = JsonConvert.DeserializeObject<List<Recommendation>>(jsonContent);

                    if (recommendations != null && recommendations.Any())
                    {
                        _logger.Debug($"Successfully parsed {recommendations.Count} recommendations from {ProviderName}");
                        return SanitizeRecommendations(recommendations);
                    }
                }

                // Fallback to parsing as markdown code block
                return ParseMarkdownRecommendations(content);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to parse recommendations from {ProviderName}: {ex.Message}");
                return ParseMarkdownRecommendations(content);
            }
        }

        protected virtual List<Recommendation> ParseMarkdownRecommendations(string content)
        {
            try
            {
                // Extract JSON from markdown code blocks
                var patterns = new[] { @"```json\s*(.*?)\s*```", @"```\s*(.*?)\s*```" };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        content, pattern,
                        System.Text.RegularExpressions.RegexOptions.Singleline);

                    if (match.Success)
                    {
                        var jsonContent = match.Groups[1].Value;
                        var recommendations = JsonConvert.DeserializeObject<List<Recommendation>>(jsonContent);

                        if (recommendations != null && recommendations.Any())
                        {
                            return SanitizeRecommendations(recommendations);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Markdown parsing failed for {ProviderName}: {ex.Message}");
            }

            return new List<Recommendation>();
        }

        protected virtual List<Recommendation> SanitizeRecommendations(List<Recommendation> recommendations)
        {
            var sanitized = new List<Recommendation>();

            foreach (var rec in recommendations)
            {
                if (string.IsNullOrWhiteSpace(rec.Artist) || string.IsNullOrWhiteSpace(rec.Album))
                    continue;

                // Clean up the recommendation
                rec.Artist = rec.Artist.Trim();
                rec.Album = rec.Album.Trim();
                rec.Genre = rec.Genre?.Trim() ?? "Unknown";
                rec.Reason = rec.Reason?.Trim() ?? "AI recommendation";

                // Ensure confidence is in valid range
                if (rec.Confidence <= 0 || rec.Confidence > 1)
                    rec.Confidence = 0.8;

                sanitized.Add(rec);
            }

            return sanitized;
        }
    }
}