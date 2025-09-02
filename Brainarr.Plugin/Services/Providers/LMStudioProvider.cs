using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// LM Studio provider implementation for local music recommendations with GUI interface.
    /// Provides an easy-to-use desktop application for running local AI models.
    /// </summary>
    /// <remarks>
    /// Requires LM Studio to be installed and running (https://lmstudio.ai).
    /// Supports a wide variety of models from Hugging Face with automatic downloading.
    /// Offers a user-friendly GUI for model management and configuration.
    /// Default URL: http://localhost:1234
    /// </remarks>
    public class LMStudioProvider : IAIProvider
    {
        private readonly string _baseUrl;
        private string _model;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly IRecommendationValidator _validator;

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "LM Studio";

        public LMStudioProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger, IRecommendationValidator? validator = null)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultLMStudioUrl;
            _model = model ?? BrainarrConstants.DefaultLMStudioModel;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
            _validator = validator ?? new RecommendationValidator(logger);
            
            _logger.Info($"LMStudioProvider initialized: URL={_baseUrl}, Model={_model}");
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            _logger.Debug($"LM Studio: Attempting connection to {_baseUrl} with model {_model}");
            
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/v1/chat/completions")
                    .Accept(HttpAccept.Json)
                    .SetHeader("Content-Type", "application/json")
                    .Post()
                    .Build();
                
                // Set timeout for AI request
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.DefaultAITimeout);

                // Detect if the prompt requests artist-only recommendations
                var promptLower = (prompt ?? string.Empty).ToLowerInvariant();
                var allowArtistOnly =
                    promptLower.Contains("artist recommendations") ||
                    promptLower.Contains("focus on artists") ||
                    (promptLower.Contains("provide exactly") && promptLower.Contains("artist"));

                var payload = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content =
                            "You are a music recommendation expert. Always respond with a valid JSON array. " +
                            "For album requests include artist, album, genre, year, confidence, reason. " +
                            "For artist requests include artist, genre, confidence, reason." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.7,
                    max_tokens = 2000,
                    stream = false
                };

                request.SetContent(JsonConvert.SerializeObject(payload));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.MaxAITimeout);

                var response = await _httpClient.ExecuteAsync(request);
                _logger.Debug($"LM Studio: Connection response - Status: {response.StatusCode}, Content Length: {response.Content?.Length ?? 0}");
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Strip BOM if present
                    var content = response.Content?.TrimStart('\ufeff') ?? "";
                    
                    _logger.Info($"LM Studio response received, content length: {content.Length}");
                    _logger.Debug($"LM Studio response structure validated");
                    
                    var json = JObject.Parse(content);
                    _logger.Debug($"LM Studio response contains {json.Properties().Count()} properties");
                    
                    if (json["choices"] is JArray choices && choices.Count > 0)
                    {
                        var firstChoice = choices[0] as JObject;
                        if (firstChoice?["message"]?["content"] != null)
                        {
                            var messageContent = firstChoice["message"]["content"].ToString();
                            _logger.Debug($"LM Studio content extracted, length: {messageContent.Length}");
                            
                            var recommendations = ParseRecommendations(messageContent, allowArtistOnly);
                            _logger.Info($"Parsed {recommendations.Count} recommendations from LM Studio");
                            return recommendations;
                        }
                        else
                        {
                            _logger.Warn("LM Studio response missing message content");
                        }
                    }
                    else
                    {
                        _logger.Warn("LM Studio response missing choices array or empty");
                    }
                }
                
                _logger.Error($"Failed to get recommendations from LM Studio: {response.StatusCode}");
                return new List<Recommendation>();
            }
            catch (HttpRequestException ex)
            {
                _logger.Error(ex, $"HTTP error calling LM Studio at {_baseUrl}: {ex.Message}");
                return new List<Recommendation>();
            }
            catch (TaskCanceledException ex)
            {
                _logger.Error(ex, $"Request to LM Studio timed out after {BrainarrConstants.MaxAITimeout} seconds");
                return new List<Recommendation>();
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "Failed to parse LM Studio response as JSON");
                return new List<Recommendation>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error calling LM Studio");
                return new List<Recommendation>();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/v1/models").Build();
                var response = await _httpClient.ExecuteAsync(request);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        protected List<Recommendation> ParseRecommendations(string response, bool allowArtistOnly)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                _logger.Info($"[LM Studio] Parsing recommendations from response: {response?.Substring(0, Math.Min(200, response?.Length ?? 0))}...");
                
                // Try to parse as JSON first
                if (response.Contains("[") && response.Contains("]"))
                {
                    var startIndex = response.IndexOf('[');
                    var endIndex = response.LastIndexOf(']') + 1;
                    if (startIndex >= 0 && endIndex > startIndex)
                    {
                        var jsonStr = response.Substring(startIndex, endIndex - startIndex);
                        _logger.Info($"[LM Studio] Extracted JSON: {jsonStr.Substring(0, Math.Min(300, jsonStr.Length))}...");
                        
                        var items = JsonConvert.DeserializeObject<JArray>(jsonStr);
                        _logger.Info($"[LM Studio] Deserialized {items.Count} items from JSON");
                        
                        foreach (var item in items)
                        {
                            if (item.Type == JTokenType.String && allowArtistOnly)
                            {
                                var name = item.ToString();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    var recStr = new Recommendation
                                    {
                                        Artist = name,
                                        Album = string.Empty,
                                        Genre = "Unknown",
                                        Confidence = 0.7,
                                        Reason = ""
                                    };
                                    if (_validator.ValidateRecommendation(recStr, allowArtistOnly: true))
                                    {
                                        _logger.Debug($"[LM Studio] Parsed artist-only recommendation: {recStr.Artist}");
                                        recommendations.Add(recStr);
                                    }
                                }
                                continue;
                            }

                            var rec = new Recommendation
                            {
                                Artist = item["artist"]?.ToString() ?? "Unknown",
                                Album = item["album"]?.ToString() ?? string.Empty,
                                Genre = item["genre"]?.ToString() ?? "Unknown",
                                Confidence = item["confidence"]?.Value<double>() ?? 0.7,
                                Reason = item["reason"]?.ToString() ?? ""
                            };
                            
                            if (_validator.ValidateRecommendation(rec, allowArtistOnly: allowArtistOnly))
                            {
                                _logger.Debug($"[LM Studio] Parsed recommendation: {rec.Artist} - {rec.Album}");
                                recommendations.Add(rec);
                            }
                            else
                            {
                                _logger.Debug($"[LM Studio] Filtered out invalid recommendation: {rec.Artist} - {rec.Album}");
                            }
                        }
                    }
                }
                else
                {
                    _logger.Info("[LM Studio] No JSON array found, trying text parsing");
                    // Fallback to simple text parsing
                    var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if ((line.Contains('-') || line.Contains('–')) || (allowArtistOnly && line.IndexOfAny(new[] { '-', '–' }) < 0))
                        {
                            if (allowArtistOnly && !(line.Contains('-') || line.Contains('–')))
                            {
                                var artistOnly = line.Trim().Trim('"', '\'', '*', '•', '.', ' ');
                                if (!string.IsNullOrWhiteSpace(artistOnly))
                                {
                                    var rec = new Recommendation
                                    {
                                        Artist = artistOnly,
                                        Album = string.Empty,
                                        Genre = "Unknown",
                                        Confidence = 0.7,
                                        Reason = ""
                                    };
                                    if (_validator.ValidateRecommendation(rec, allowArtistOnly: true))
                                    {
                                        _logger.Debug($"[LM Studio] Text parsed artist recommendation: {rec.Artist}");
                                        recommendations.Add(rec);
                                    }
                                }
                            }
                            else
                            {
                                var parts = line.Split(new[] { '-', '–' }, 2);
                                if (parts.Length == 2)
                                {
                                    var rec = new Recommendation
                                    {
                                        Artist = parts[0].Trim().Trim('"', '\'', '*', '•', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.'),
                                        Album = parts[1].Trim().Trim('"', '\''),
                                        Genre = "Unknown",
                                        Confidence = 0.7,
                                        Reason = ""
                                    };
                                    
                                    if (_validator.ValidateRecommendation(rec, allowArtistOnly: false))
                                    {
                                        _logger.Debug($"[LM Studio] Text parsed recommendation: {rec.Artist} - {rec.Album}");
                                        recommendations.Add(rec);
                                    }
                                    else
                                    {
                                        _logger.Debug($"[LM Studio] Filtered out invalid text recommendation: {rec.Artist} - {rec.Album}");
                                    }
                                }
                            }
                        }
                    }
                }
                
                _logger.Info($"[LM Studio] Final recommendation count: {recommendations.Count}");
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "[LM Studio] Failed to parse recommendations as JSON");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[LM Studio] Unexpected error parsing recommendations");
            }
            
            return recommendations;
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"LM Studio model updated to: {modelName}");
            }
        }
    }
}
